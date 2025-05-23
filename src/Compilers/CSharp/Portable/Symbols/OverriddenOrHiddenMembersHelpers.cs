﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.CSharp.Symbols.Retargeting;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Encapsulates the MakeOverriddenOrHiddenMembers functionality for methods, properties (including indexers), 
    /// and events.
    /// </summary>
    internal static class OverriddenOrHiddenMembersHelpers
    {
        internal static OverriddenOrHiddenMembersResult MakeOverriddenOrHiddenMembers(this MethodSymbol member)
        {
            return MakeOverriddenOrHiddenMembersWorker(member);
        }

        internal static OverriddenOrHiddenMembersResult MakeOverriddenOrHiddenMembers(this PropertySymbol member)
        {
            return MakeOverriddenOrHiddenMembersWorker(member);
        }

        internal static OverriddenOrHiddenMembersResult MakeOverriddenOrHiddenMembers(this EventSymbol member)
        {
            return MakeOverriddenOrHiddenMembersWorker(member);
        }

        /// <summary>
        /// Walk up the type hierarchy from ContainingType and list members that this
        /// member either overrides (accessible members with the same signature, if this
        /// member is declared "override") or hides (accessible members with the same name
        /// but different kinds, plus members that would be in the overrides list if
        /// this member were not declared "override").
        /// 
        /// Members in the overridden list may be non-virtual or may have different
        /// accessibilities, types, accessors, etc.  They are really candidates to be
        /// overridden.
        /// 
        /// Members in the hidden list are definitely hidden.
        /// 
        /// Members in the runtime overridden list are indistinguishable from the members
        /// in the overridden list from the point of view of the runtime (see
        /// FindOtherOverriddenMethodsInContainingType for details).
        /// </summary>
        /// <remarks>
        /// In the presence of non-C# types, the meaning of "same signature" is rather
        /// complicated.  If this member isn't from source, then it refers to the runtime's
        /// notion of signature (i.e. including return type, custom modifiers, etc).
        /// If this member is from source, then the process is (conceptually) as follows.
        /// 
        /// 1) Walk up the type hierarchy, recording all matching members with the same
        ///    signature, ignoring custom modifiers and return type.  Stop if a hidden
        ///    member is encountered.
        /// 2) Apply the following "tie-breaker" rules until you have at most one member,
        ///    a) Prefer members in more derived types.
        ///    b) Prefer an exact custom modifier match (i.e. none, for a source member).
        ///    c) Prefer fewer custom modifiers (values/positions don't matter, just count).
        ///    d) Prefer earlier in GetMembers order (within the same type).
        /// 3) If a member remains, search its containing type for other members that
        ///    have the same C# signature (overridden members) or runtime signature
        ///    (runtime overridden members).
        /// 
        /// In metadata, properties participate in overriding only through their accessors.
        /// That is, property/event accessors may implicitly or explicitly override other methods
        /// and a property/event can be considered to override another property/event if its accessors
        /// override those of the other property/event.
        /// This implementation (like Dev10) will not follow that approach.  Instead, it is
        /// based on spec section 10.7.5, which treats properties as entities in their own
        /// right.  If all property/event accessors have conventional names in metadata and nothing
        /// "unusual" is done with explicit overriding, this approach should produce the same
        /// results as an implementation based on accessor overriding.
        /// </remarks>
        private static OverriddenOrHiddenMembersResult MakeOverriddenOrHiddenMembersWorker(Symbol member)
        {
            Debug.Assert(member.Kind == SymbolKind.Method || member.Kind == SymbolKind.Property || member.Kind == SymbolKind.Event);

            if (!CanOverrideOrHide(member))
            {
                return OverriddenOrHiddenMembersResult.Empty;
            }

            if (member.IsAccessor())
            {
                // Accessors are handled specially - see MakePropertyAccessorOverriddenOrHiddenMembers for details.
                MethodSymbol accessor = member as MethodSymbol;
                Symbol associatedPropertyOrEvent = accessor.AssociatedSymbol;
                if ((object)associatedPropertyOrEvent != null)
                {
                    if (associatedPropertyOrEvent.Kind == SymbolKind.Property)
                    {
                        return MakePropertyAccessorOverriddenOrHiddenMembers(accessor, (PropertySymbol)associatedPropertyOrEvent);
                    }
                    else
                    {
                        Debug.Assert(associatedPropertyOrEvent.Kind == SymbolKind.Event);
                        return MakeEventAccessorOverriddenOrHiddenMembers(accessor, (EventSymbol)associatedPropertyOrEvent);
                    }
                }
            }

            Debug.Assert(!member.IsAccessor());

            NamedTypeSymbol containingType = member.ContainingType;

            // NOTE: In other areas of the compiler, we check whether the member is from a specific compilation.
            // We could do the same thing here, but that would mean that callers of the public API would have
            // to pass in a Compilation object when asking about overriding or hiding.  This extra cost eliminates
            // the small benefit of getting identical answers from "imported" symbols, regardless of whether they
            // are imported as source or metadata symbols.
            //
            // We believe that source and metadata behaviors agree for correct code, modulo accomodations for
            // runtime bugs (such as https://github.com/dotnet/roslyn/issues/45453) on older platforms.
            // In incorrect code,
            // the source behavior is somewhat more generous (e.g. accepting a method with the wrong return type),
            // but we do not guarantee that incorrect source will be treated in the same way as incorrect metadata.
            bool memberIsFromSomeCompilation = member.Dangerous_IsFromSomeCompilation;

            if (containingType.IsInterface)
            {
                return MakeInterfaceOverriddenOrHiddenMembers(member, memberIsFromSomeCompilation);
            }

            ArrayBuilder<Symbol> hiddenBuilder;
            ImmutableArray<Symbol> overriddenMembers;
            FindOverriddenOrHiddenMembers(member, containingType, memberIsFromSomeCompilation, out hiddenBuilder, out overriddenMembers);

            ImmutableArray<Symbol> hiddenMembers = hiddenBuilder == null ? ImmutableArray<Symbol>.Empty : hiddenBuilder.ToImmutableAndFree();
            return OverriddenOrHiddenMembersResult.Create(overriddenMembers, hiddenMembers);
        }

        private static void FindOverriddenOrHiddenMembers(Symbol member, NamedTypeSymbol containingType, bool memberIsFromSomeCompilation,
            out ArrayBuilder<Symbol> hiddenBuilder,
            out ImmutableArray<Symbol> overriddenMembers)
        {
            Symbol bestMatch = null;
            hiddenBuilder = null;

            // A specific override exact match candidate, if one is known. This supports covariant returns, for which signature
            // matching is not sufficient. This member is treated as being as good as an exact match.
            Symbol knownOverriddenMember = member switch
            {
                MethodSymbol method => KnownOverriddenClassMethod(method),
                PEPropertySymbol { GetMethod: PEMethodSymbol { ExplicitlyOverriddenClassMethod: { AssociatedSymbol: PropertySymbol overriddenProperty } } } => overriddenProperty,
                RetargetingPropertySymbol { GetMethod: RetargetingMethodSymbol { ExplicitlyOverriddenClassMethod: { AssociatedSymbol: PropertySymbol overriddenProperty } } } => overriddenProperty,
                _ => null
            };

            for (NamedTypeSymbol currType = containingType.BaseTypeNoUseSiteDiagnostics;
                (object)currType != null && (object)bestMatch == null && hiddenBuilder == null;
                currType = currType.BaseTypeNoUseSiteDiagnostics)
            {
                bool unused;
                FindOverriddenOrHiddenMembersInType(
                    member,
                    memberIsFromSomeCompilation,
                    containingType,
                    knownOverriddenMember,
                    currType,
                    out bestMatch,
                    out unused,
                    out hiddenBuilder);
            }

            // Based on bestMatch, find other methods that will be overridden, hidden, or runtime overridden
            // (in bestMatch.ContainingType).
            FindRelatedMembers(member.IsOverride, memberIsFromSomeCompilation, member, bestMatch, out overriddenMembers, ref hiddenBuilder);
        }

        public static Symbol FindFirstHiddenMemberIfAny(Symbol member, bool memberIsFromSomeCompilation)
        {
            ArrayBuilder<Symbol> hiddenBuilder;
            FindOverriddenOrHiddenMembers(member, member.ContainingType, memberIsFromSomeCompilation, out hiddenBuilder,
                overriddenMembers: out _);

            Symbol result = hiddenBuilder?.FirstOrDefault();
            hiddenBuilder?.Free();

            return result;
        }

        /// <summary>
        /// Compute a candidate overridden method when a method knows what method it is intended to
        /// override. This makes a particular difference when covariant returns are used, in which
        /// case the signature matching rules would not compute the correct overridden method.
        /// </summary>
        private static MethodSymbol KnownOverriddenClassMethod(MethodSymbol method) =>
            method switch
            {
                PEMethodSymbol m => m.ExplicitlyOverriddenClassMethod,
                RetargetingMethodSymbol m => m.ExplicitlyOverriddenClassMethod,
                _ => null
            };

        /// <summary>
        /// In the CLI, accessors are just regular methods and their overriding/hiding rules are the same as for
        /// regular methods.  In C#, however, accessors are intimately connected with their corresponding properties.
        /// Rather than walking up the type hierarchy from the containing type of this accessor, looking for members
        /// with the same name, MakePropertyAccessorOverriddenOrHiddenMembers delegates to the associated property.
        /// For an accessor to hide a member, the hidden member must be a corresponding accessor on a property hidden
        /// by the associated property.  For an accessor to override a member, the overridden member must be a
        /// corresponding accessor on a property (directly or indirectly) overridden by the associated property.
        /// 
        /// Example 1:
        /// 
        /// public class A { public virtual int P { get; set; } }
        /// public class B : A { public override int P { get { return 1; } } } //get only
        /// public class C : B { public override int P { set { } } } // set only
        /// 
        /// C.P.set overrides A.P.set because C.P.set is the setter of C.P, which overrides B.P,
        /// which overrides A.P, which has A.P.set as a setter.
        /// 
        /// Example 2:
        /// 
        /// public class A { public virtual int P { get; set; } }
        /// public class B : A { public new virtual int P { get { return 1; } } } //get only
        /// public class C : B { public override int P { set { } } } // set only
        /// 
        /// C.P.set does not override any method because C.P overrides B.P, which has no setter
        /// and does not override a property.
        /// </summary>
        /// <param name="accessor">This accessor.</param>
        /// <param name="associatedProperty">The property associated with this accessor.</param>
        /// <returns>Members overridden or hidden by this accessor.</returns>
        /// <remarks>
        /// This method is intended to return values consistent with the definition of C#, which
        /// may differ from the actual meaning at runtime.
        /// 
        /// Note: we don't need a different path for interfaces - Property.OverriddenOrHiddenMembers handles that.
        /// </remarks>
        private static OverriddenOrHiddenMembersResult MakePropertyAccessorOverriddenOrHiddenMembers(MethodSymbol accessor, PropertySymbol associatedProperty)
        {
            Debug.Assert(accessor.IsAccessor());
            Debug.Assert((object)associatedProperty != null);

            bool accessorIsGetter = accessor.MethodKind == MethodKind.PropertyGet;

            MethodSymbol overriddenAccessor = null;
            ArrayBuilder<Symbol> hiddenBuilder = null;

            OverriddenOrHiddenMembersResult hiddenOrOverriddenByProperty = associatedProperty.OverriddenOrHiddenMembers;

            foreach (Symbol hiddenByProperty in hiddenOrOverriddenByProperty.HiddenMembers)
            {
                if (hiddenByProperty.Kind == SymbolKind.Property)
                {
                    // If we're looking at the associated property of this method (vs a property
                    // it overrides), then record the corresponding accessor (if any) as hidden.
                    PropertySymbol propertyHiddenByProperty = (PropertySymbol)hiddenByProperty;
                    MethodSymbol correspondingAccessor = accessorIsGetter ? propertyHiddenByProperty.GetMethod : propertyHiddenByProperty.SetMethod;
                    if ((object)correspondingAccessor != null)
                    {
                        AccessOrGetInstance(ref hiddenBuilder).Add(correspondingAccessor);
                    }
                }
            }

            if (hiddenOrOverriddenByProperty.OverriddenMembers.Any())
            {
                // CONSIDER: Do something more sensible if there are multiple overridden members?  Already an error case.
                PropertySymbol propertyOverriddenByProperty = (PropertySymbol)hiddenOrOverriddenByProperty.OverriddenMembers[0];
                MethodSymbol correspondingAccessor = accessorIsGetter ?
                    propertyOverriddenByProperty.GetOwnOrInheritedGetMethod() :
                    propertyOverriddenByProperty.GetOwnOrInheritedSetMethod();
                if ((object)correspondingAccessor != null)
                {
                    overriddenAccessor = correspondingAccessor;
                }
            }

            // There's a detailed comment in MakeOverriddenOrHiddenMembersWorker(Symbol) concerning why this predicate is appropriate.
            bool accessorIsFromSomeCompilation = accessor.Dangerous_IsFromSomeCompilation;
            ImmutableArray<Symbol> overriddenAccessors = ImmutableArray<Symbol>.Empty;
            if ((object)overriddenAccessor != null && IsOverriddenSymbolAccessible(overriddenAccessor, accessor.ContainingType) &&
                isAccessorOverride(accessor, overriddenAccessor))
            {
                FindRelatedMembers(
                    accessor.IsOverride, accessorIsFromSomeCompilation, accessor, overriddenAccessor, out overriddenAccessors, ref hiddenBuilder);
            }

            ImmutableArray<Symbol> hiddenMembers = hiddenBuilder == null ? ImmutableArray<Symbol>.Empty : hiddenBuilder.ToImmutableAndFree();
            return OverriddenOrHiddenMembersResult.Create(overriddenAccessors, hiddenMembers);

            bool isAccessorOverride(MethodSymbol accessor, MethodSymbol overriddenAccessor)
            {
                if (accessorIsFromSomeCompilation)
                {
                    return MemberSignatureComparer.CSharpAccessorOverrideComparer.Equals(accessor, overriddenAccessor); //NB: custom comparer
                }

                if (overriddenAccessor.Equals(KnownOverriddenClassMethod(accessor), TypeCompareKind.AllIgnoreOptions))
                {
                    return true;
                }

                return MemberSignatureComparer.RuntimeSignatureComparer.Equals(accessor, overriddenAccessor);
            }
        }

        /// <summary>
        /// In the CLI, accessors are just regular methods and their overriding/hiding rules are the same as for
        /// regular methods.  In C#, however, accessors are intimately connected with their corresponding events.
        /// Rather than walking up the type hierarchy from the containing type of this accessor, looking for members
        /// with the same name, MakeEventAccessorOverriddenOrHiddenMembers delegates to the associated event.
        /// For an accessor to hide a member, the hidden member must be a corresponding accessor on a event hidden
        /// by the associated event.  For an accessor to override a member, the overridden member must be a
        /// corresponding accessor on a event (directly or indirectly) overridden by the associated event.
        /// </summary>
        /// <param name="accessor">This accessor.</param>
        /// <param name="associatedEvent">The event associated with this accessor.</param>
        /// <returns>Members overridden or hidden by this accessor.</returns>
        /// <remarks>
        /// This method is intended to return values consistent with the definition of C#, which
        /// may differ from the actual meaning at runtime.
        /// 
        /// Note: we don't need a different path for interfaces - Event.OverriddenOrHiddenMembers handles that.
        /// 
        /// CONSIDER: It is an error for an event to have only one accessor.  Currently, we mimic the behavior for
        /// properties, for consistency, but an alternative approach would be to say that nothing is overridden.
        /// 
        /// CONSIDER: is there a way to share code with MakePropertyAccessorOverriddenOrHiddenMembers?
        /// </remarks>
        private static OverriddenOrHiddenMembersResult MakeEventAccessorOverriddenOrHiddenMembers(MethodSymbol accessor, EventSymbol associatedEvent)
        {
            Debug.Assert(accessor.IsAccessor());
            Debug.Assert((object)associatedEvent != null);

            bool accessorIsAdder = accessor.MethodKind == MethodKind.EventAdd;

            MethodSymbol overriddenAccessor = null;
            ArrayBuilder<Symbol> hiddenBuilder = null;

            OverriddenOrHiddenMembersResult hiddenOrOverriddenByEvent = associatedEvent.OverriddenOrHiddenMembers;

            foreach (Symbol hiddenByEvent in hiddenOrOverriddenByEvent.HiddenMembers)
            {
                if (hiddenByEvent.Kind == SymbolKind.Event)
                {
                    // If we're looking at the associated event of this method (vs a event
                    // it overrides), then record the corresponding accessor (if any) as hidden.
                    EventSymbol eventHiddenByEvent = (EventSymbol)hiddenByEvent;
                    MethodSymbol correspondingAccessor = accessorIsAdder ? eventHiddenByEvent.AddMethod : eventHiddenByEvent.RemoveMethod;
                    if ((object)correspondingAccessor != null)
                    {
                        AccessOrGetInstance(ref hiddenBuilder).Add(correspondingAccessor);
                    }
                }
            }

            if (hiddenOrOverriddenByEvent.OverriddenMembers.Any())
            {
                // CONSIDER: Do something more sensible if there are multiple overridden members?  Already an error case.
                EventSymbol eventOverriddenByEvent = (EventSymbol)hiddenOrOverriddenByEvent.OverriddenMembers[0];
                MethodSymbol correspondingAccessor = eventOverriddenByEvent.GetOwnOrInheritedAccessor(accessorIsAdder);
                if ((object)correspondingAccessor != null)
                {
                    overriddenAccessor = correspondingAccessor;
                }
            }

            // There's a detailed comment in MakeOverriddenOrHiddenMembersWorker(Symbol) concerning why this predicate is appropriate.
            bool accessorIsFromSomeCompilation = accessor.Dangerous_IsFromSomeCompilation;
            ImmutableArray<Symbol> overriddenAccessors = ImmutableArray<Symbol>.Empty;
            if ((object)overriddenAccessor != null && IsOverriddenSymbolAccessible(overriddenAccessor, accessor.ContainingType) &&
                    (accessorIsFromSomeCompilation
                        ? MemberSignatureComparer.CSharpAccessorOverrideComparer.Equals(accessor, overriddenAccessor) //NB: custom comparer
                        : MemberSignatureComparer.RuntimeSignatureComparer.Equals(accessor, overriddenAccessor)))
            {
                FindRelatedMembers(
                    accessor.IsOverride, accessorIsFromSomeCompilation, accessor, overriddenAccessor, out overriddenAccessors, ref hiddenBuilder);
            }

            ImmutableArray<Symbol> hiddenMembers = hiddenBuilder == null ? ImmutableArray<Symbol>.Empty : hiddenBuilder.ToImmutableAndFree();
            return OverriddenOrHiddenMembersResult.Create(overriddenAccessors, hiddenMembers);
        }

        /// <summary>
        /// There are two key reasons why interface overriding/hiding is different from class overriding/hiding:
        ///   1) interface members never override other members; and
        ///   2) interfaces can extend multiple interfaces.
        /// The first difference doesn't require any special handling - as long as the members have IsOverride=false,
        /// the code for class overriding/hiding does the right thing.
        /// The second difference is more problematic.  For one thing, an interface member can hide a different member in
        /// each base interface.  We only report the first one, but we need to expose all of them in the API.  More importantly,
        /// multiple inheritance raises the possibility of diamond inheritance.  Spec section 13.2.5, Interface member access,
        /// says: "The intuitive rule for hiding in multiple-inheritance interfaces is simply this: If a member is hidden in any
        /// access path, it is hidden in all access paths."  For example, consider the following interfaces:
        /// 
        /// interface I0 { void M(); }
        /// interface I1 : I0 { void M(); }
        /// interface I2 : I0, I1 { void M(); }
        /// 
        /// I2.M does not hide I0.M, because it is already hidden by I1.M.  To make this work, we need to traverse the graph
        /// of ancestor interfaces in topological order and flag ones later in the enumeration that are hidden along some path.
        /// </summary>
        /// <remarks>
        /// See SymbolPreparer::checkIfaceHiding.
        /// </remarks>
        internal static OverriddenOrHiddenMembersResult MakeInterfaceOverriddenOrHiddenMembers(Symbol member, bool memberIsFromSomeCompilation)
        {
            Debug.Assert(!member.IsAccessor());

            NamedTypeSymbol containingType = member.ContainingType;
            Debug.Assert(containingType.IsInterfaceType());

            PooledHashSet<NamedTypeSymbol> membersOfOtherKindsHidden = PooledHashSet<NamedTypeSymbol>.GetInstance();
            PooledHashSet<NamedTypeSymbol> allMembersHidden = PooledHashSet<NamedTypeSymbol>.GetInstance(); // Implies membersOfOtherKindsHidden.

            ArrayBuilder<Symbol> hiddenBuilder = null;

            foreach (NamedTypeSymbol currType in containingType.AllInterfacesNoUseSiteDiagnostics) // NB: topologically sorted
            {
                if (allMembersHidden.Contains(currType))
                {
                    continue;
                }

                Symbol currTypeBestMatch;
                bool currTypeHasSameKindNonMatch;
                ArrayBuilder<Symbol> currTypeHiddenBuilder;

                FindOverriddenOrHiddenMembersInType(
                    member,
                    memberIsFromSomeCompilation,
                    containingType,
                    knownOverriddenMember: null,
                    currType,
                    out currTypeBestMatch,
                    out currTypeHasSameKindNonMatch,
                    out currTypeHiddenBuilder);

                bool haveBestMatch = (object)currTypeBestMatch != null;

                if (haveBestMatch)
                {
                    // If our base interface contains a matching member of the same kind, 
                    // then we don't need to look any further up this subtree.
                    foreach (var hidden in currType.AllInterfacesNoUseSiteDiagnostics)
                    {
                        allMembersHidden.Add(hidden);
                    }

                    AccessOrGetInstance(ref hiddenBuilder).Add(currTypeBestMatch);
                }

                if (currTypeHiddenBuilder != null)
                {
                    // If our base interface contains a matching member of a different kind, then
                    // it will hide all members that aren't of that kind further up the chain.
                    // As a result, nothing of our kind will be visible and we can stop looking.
                    if (!membersOfOtherKindsHidden.Contains(currType))
                    {
                        if (!haveBestMatch)
                        {
                            foreach (var hidden in currType.AllInterfacesNoUseSiteDiagnostics)
                            {
                                allMembersHidden.Add(hidden);
                            }
                        }

                        AccessOrGetInstance(ref hiddenBuilder).AddRange(currTypeHiddenBuilder);
                    }

                    currTypeHiddenBuilder.Free();
                }
                else if (currTypeHasSameKindNonMatch && !haveBestMatch)
                {
                    // If our base interface contains a (non-matching) member of the same kind, then
                    // it will hide all members that aren't of that kind further up the chain.
                    foreach (var hidden in currType.AllInterfacesNoUseSiteDiagnostics)
                    {
                        membersOfOtherKindsHidden.Add(hidden);
                    }
                }
            }

            membersOfOtherKindsHidden.Free();
            allMembersHidden.Free();

            // Based on bestMatch, find other methods that will be overridden, hidden, or runtime overridden
            // (in bestMatch.ContainingType).
            ImmutableArray<Symbol> overriddenMembers = ImmutableArray<Symbol>.Empty;

            if (hiddenBuilder != null)
            {
                ArrayBuilder<Symbol> hiddenAndRelatedBuilder = null;
                foreach (Symbol hidden in hiddenBuilder)
                {
                    FindRelatedMembers(member.IsOverride, memberIsFromSomeCompilation, member, hidden, out overriddenMembers, ref hiddenAndRelatedBuilder);
                    Debug.Assert(overriddenMembers.Length == 0);
                }
                hiddenBuilder.Free();
                hiddenBuilder = hiddenAndRelatedBuilder;
            }

            Debug.Assert(overriddenMembers.IsEmpty);

            ImmutableArray<Symbol> hiddenMembers = hiddenBuilder == null ? ImmutableArray<Symbol>.Empty : hiddenBuilder.ToImmutableAndFree();
            return OverriddenOrHiddenMembersResult.Create(overriddenMembers, hiddenMembers);
        }

        /// <summary>
        /// Look for overridden or hidden members in a specific type.
        /// </summary>
        /// <param name="member">Member that is hiding or overriding.</param>
        /// <param name="memberIsFromSomeCompilation">True if member is from the current compilation.</param>
        /// <param name="memberContainingType">The type that contains member (member.ContainingType).</param>
        /// <param name="knownOverriddenMember">The known overridden member (e.g. in the presence of a metadata methodimpl).</param>
        /// <param name="currType">The type to search.</param>
        /// <param name="currTypeBestMatch">
        /// A member with the same signature if currTypeHasExactMatch is true,
        /// a member with (a minimal number of) different custom modifiers if there is one,
        /// and null otherwise.</param>
        /// <param name="currTypeHasSameKindNonMatch">True if there's a member with the same name and kind that is not a match.</param>
        /// <param name="hiddenBuilder">Hidden members (same name, different kind) will be added to this builder.</param>
        /// <remarks>
        /// There is some similarity between this member and TypeSymbol.FindPotentialImplicitImplementationMemberDeclaredInType.
        /// When making changes to this member, think about whether or not they should also be applied in TypeSymbol.
        /// 
        /// In incorrect or imported code, it is possible that both currTypeBestMatch and hiddenBuilder will be populated.
        /// </remarks>
        private static void FindOverriddenOrHiddenMembersInType(
            Symbol member,
            bool memberIsFromSomeCompilation,
            NamedTypeSymbol memberContainingType,
            Symbol knownOverriddenMember,
            NamedTypeSymbol currType,
            out Symbol currTypeBestMatch,
            out bool currTypeHasSameKindNonMatch,
            out ArrayBuilder<Symbol> hiddenBuilder)
        {
            Debug.Assert(!member.IsAccessor());

            currTypeBestMatch = null;
            currTypeHasSameKindNonMatch = false;
            hiddenBuilder = null;

            bool currTypeHasExactMatch = false;
            int minCustomModifierCount = int.MaxValue;

            IEqualityComparer<Symbol> exactMatchComparer = memberIsFromSomeCompilation
                ? MemberSignatureComparer.CSharpCustomModifierOverrideComparer
                : MemberSignatureComparer.RuntimePlusRefOutSignatureComparer;

            IEqualityComparer<Symbol> fallbackComparer = memberIsFromSomeCompilation
                ? MemberSignatureComparer.CSharpOverrideComparer
                : MemberSignatureComparer.RuntimeSignatureComparer;

            SymbolKind memberKind = member.Kind;
            int memberArity = member.GetMemberArity();

            foreach (Symbol otherMember in currType.GetMembers(member.Name))
            {
                if (!IsOverriddenSymbolAccessible(otherMember, memberContainingType))
                {
                    //do nothing
                }
                else if (otherMember.IsAccessor() && !((MethodSymbol)otherMember).IsIndexedPropertyAccessor())
                {
                    //Indexed property accessors can be overridden or hidden by non-accessors.
                    //do nothing - no interaction between accessors and non-accessors
                }
                else if (otherMember.Kind != memberKind)
                {
                    // NOTE: generic methods can hide things with arity 0.
                    // From CSemanticChecker::FindSymHiddenByMethPropAgg
                    int otherMemberArity = otherMember.GetMemberArity();
                    if (otherMemberArity == memberArity || (memberKind == SymbolKind.Method && otherMemberArity == 0))
                    {
                        AddHiddenMemberIfApplicable(ref hiddenBuilder, member, otherMember);
                    }
                }
                else if (!currTypeHasExactMatch)
                {
                    switch (memberKind)
                    {
                        case SymbolKind.Field:
                            currTypeHasExactMatch = true;
                            currTypeBestMatch = otherMember;
                            break;
                        case SymbolKind.NamedType:
                            if (otherMember.GetMemberArity() == memberArity)
                            {
                                currTypeHasExactMatch = true;
                                currTypeBestMatch = otherMember;
                            }
                            break;

                        default:
                            if (otherMember.Equals(knownOverriddenMember, TypeCompareKind.AllIgnoreOptions))
                            {
                                currTypeHasExactMatch = true;
                                currTypeBestMatch = otherMember;
                            }

                            // We do not perform signature matching in the presence of a methodimpl
                            else if (knownOverriddenMember == null)
                            {
                                if (exactMatchComparer.Equals(member, otherMember))
                                {
                                    currTypeHasExactMatch = true;
                                    currTypeBestMatch = otherMember;
                                }
                                else if (fallbackComparer.Equals(member, otherMember))
                                {
                                    // If this method is from source, we'll also consider methods that match
                                    // without regard to custom modifiers.  If there's more than one, we'll
                                    // choose the one with the fewest custom modifiers.
                                    int methodCustomModifierCount = CustomModifierCount(otherMember);
                                    if (methodCustomModifierCount < minCustomModifierCount)
                                    {
                                        Debug.Assert(memberIsFromSomeCompilation || minCustomModifierCount == int.MaxValue, "Metadata members require exact custom modifier matches.");
                                        minCustomModifierCount = methodCustomModifierCount;
                                        currTypeBestMatch = otherMember;
                                    }
                                }
                                else
                                {
                                    currTypeHasSameKindNonMatch = true;
                                }
                            }

                            break;
                    }
                }
            }

            switch (memberKind)
            {
                case SymbolKind.Field:
                case SymbolKind.NamedType:
                    break;

                default:
                    // If the member is from metadata, then even a fallback match is an exact match.
                    // We just declined to set the flag at the time in case there was a "better" exact match.
                    // Having said that, there's no reason to update the flag, since no-one will consume it.
                    // if (!memberIsFromSomeCompilation && ((object)currTypeBestMatch != null)) currTypeHasExactMatch = true;

                    // There's a special case where we have to go back and fix up our best match.
                    // If member is from source, then it has no custom modifiers (at least,
                    // until it copies them from the member it overrides, which we're trying to
                    // compute now).  Therefore, an exact match will be one that has no custom
                    // modifiers in/on its parameters.  The best match may, however, have custom
                    // modifiers in/on its (return) type, since that wasn't considered during the
                    // signature comparison.  If that is the case, then we need to make sure that
                    // there isn't another inexact match (i.e. same signature ignoring custom 
                    // modifiers) with fewer custom modifiers.
                    // NOTE: If member is constructed, then it has already inherited custom modifiers
                    // from the underlying member and this cleanup is unnecessary.  That's why we're
                    // checking member.IsDefinition in addition to memberIsFromSomeCompilation.
                    if (currTypeHasExactMatch && memberIsFromSomeCompilation && member.IsDefinition && TypeOrReturnTypeHasCustomModifiers(currTypeBestMatch))
                    {
#if DEBUG
                        // If there were custom modifiers on the parameters, then the match wouldn't have been
                        // exact and so we would already have applied the custom modifier count as a tie-breaker.
                        foreach (ParameterSymbol param in currTypeBestMatch.GetParameters())
                        {
                            Debug.Assert(!(param.TypeWithAnnotations.CustomModifiers.Any() || param.RefCustomModifiers.Any()));
                            Debug.Assert(!param.Type.HasCustomModifiers(flagNonDefaultArraySizesOrLowerBounds: false));
                        }
#endif

                        Symbol minCustomModifierMatch = currTypeBestMatch;

                        foreach (Symbol otherMember in currType.GetMembers(member.Name))
                        {
                            if (otherMember.Kind == currTypeBestMatch.Kind && !ReferenceEquals(otherMember, currTypeBestMatch))
                            {
                                if (MemberSignatureComparer.CSharpOverrideComparer.Equals(otherMember, currTypeBestMatch))
                                {
                                    int customModifierCount = CustomModifierCount(otherMember);
                                    if (customModifierCount < minCustomModifierCount)
                                    {
                                        minCustomModifierCount = customModifierCount;
                                        minCustomModifierMatch = otherMember;
                                    }
                                }
                            }
                        }

                        currTypeBestMatch = minCustomModifierMatch;
                    }
                    break;
            }
        }

        /// <summary>
        /// If representative member is non-null and is contained in a constructed type, then find
        /// other members in the same type with the same signature.  If this is an override member,
        /// add them to the overridden and runtime overridden lists.  Otherwise, add them to the
        /// hidden list.
        /// </summary>
        private static void FindRelatedMembers(
            bool isOverride,
            bool overridingMemberIsFromSomeCompilation,
            Symbol overridingMember,
            Symbol representativeMember,
            out ImmutableArray<Symbol> overriddenMembers,
            ref ArrayBuilder<Symbol> hiddenBuilder)
        {
            overriddenMembers = ImmutableArray<Symbol>.Empty;

            if ((object)representativeMember != null)
            {
                bool needToSearchForRelated = representativeMember.Kind != SymbolKind.Field && representativeMember.Kind != SymbolKind.NamedType &&
                                              (!representativeMember.ContainingType.IsDefinition || representativeMember.IsIndexer());

                if (isOverride)
                {
                    if (needToSearchForRelated)
                    {
                        ArrayBuilder<Symbol> overriddenBuilder = ArrayBuilder<Symbol>.GetInstance();

                        overriddenBuilder.Add(representativeMember);

                        FindOtherOverriddenMethodsInContainingType(representativeMember, overridingMemberIsFromSomeCompilation, overriddenBuilder);

                        overriddenMembers = overriddenBuilder.ToImmutableAndFree();
                    }
                    else
                    {
                        overriddenMembers = ImmutableArray.Create<Symbol>(representativeMember);
                    }
                }
                else
                {
                    AddHiddenMemberIfApplicable(ref hiddenBuilder, overridingMember, representativeMember);

                    if (needToSearchForRelated)
                    {
                        FindOtherHiddenMembersInContainingType(overridingMember, representativeMember, ref hiddenBuilder);
                    }
                }
            }
        }

        /// <summary>
        /// Some kinds of methods are not considered to be hideable by certain kinds of members.
        /// Specifically, methods, properties, and types cannot hide constructors, destructors,
        /// operators, conversions, or accessors.
        /// </summary>
        private static void AddHiddenMemberIfApplicable(ref ArrayBuilder<Symbol> hiddenBuilder, Symbol hidingMember, Symbol hiddenMember)
        {
            Debug.Assert((object)hiddenMember != null);
            if (hiddenMember.Kind != SymbolKind.Method || ((MethodSymbol)hiddenMember).CanBeHiddenByMember(hidingMember))
            {
                AccessOrGetInstance(ref hiddenBuilder).Add(hiddenMember);
            }
        }

        private static ArrayBuilder<T> AccessOrGetInstance<T>(ref ArrayBuilder<T> builder)
        {
            if (builder == null)
            {
                builder = ArrayBuilder<T>.GetInstance();
            }

            return builder;
        }

        /// <summary>
        /// Having found the best member to override, we want to find members with the same signature on the
        /// best member's containing type.
        /// </summary>
        /// <param name="representativeMember">
        /// The member that we consider to be overridden (may have different custom modifiers from the overriding member).
        /// Assumed to already be in the overridden and runtime overridden lists.
        /// </param>
        /// <param name="overridingMemberIsFromSomeCompilation">
        /// If the best match was based on the custom modifier count, rather than the custom modifiers themselves 
        /// (because the overriding member is in the current compilation), then we should use the count when determining
        /// whether the override is ambiguous.
        /// </param>
        /// <param name="overriddenBuilder">
        /// If the declaring type is constructed, it's possible that two (or more) members have the same signature
        /// (including custom modifiers).  Return a list of such members so that we can report the ambiguity.
        /// </param>
        private static void FindOtherOverriddenMethodsInContainingType(Symbol representativeMember, bool overridingMemberIsFromSomeCompilation, ArrayBuilder<Symbol> overriddenBuilder)
        {
            Debug.Assert((object)representativeMember != null);
            Debug.Assert(representativeMember.Kind == SymbolKind.Property || !representativeMember.ContainingType.IsDefinition);

            int representativeCustomModifierCount = -1;

            foreach (Symbol otherMember in representativeMember.ContainingType.GetMembers(representativeMember.Name))
            {
                if (otherMember.Kind == representativeMember.Kind)
                {
                    if (otherMember != representativeMember)
                    {
                        // NOTE: If the overriding member is from source, then we compared *counts* of custom modifiers, rather
                        // than actually comparing custom modifiers.  Hence, we should do the same thing when looking for
                        // ambiguous overrides.
                        if (overridingMemberIsFromSomeCompilation)
                        {
                            if (representativeCustomModifierCount < 0)
                            {
                                representativeCustomModifierCount = representativeMember.CustomModifierCount();
                            }

                            if (MemberSignatureComparer.CSharpOverrideComparer.Equals(otherMember, representativeMember) &&
                                otherMember.CustomModifierCount() == representativeCustomModifierCount)
                            {
                                overriddenBuilder.Add(otherMember);
                            }
                        }
                        else
                        {
                            if (MemberSignatureComparer.CSharpCustomModifierOverrideComparer.Equals(otherMember, representativeMember))
                            {
                                overriddenBuilder.Add(otherMember);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Having found that we are hiding a method with exactly the same signature
        /// (including custom modifiers), we want to find methods with the same signature
        /// on the declaring type because they will also be hidden.
        /// (If the declaring type is constructed, it's possible that two or more
        /// methods have the same signature (including custom modifiers).)
        /// (If the representative member is an indexer, it's possible that two or more
        /// properties have the same signature (including custom modifiers, even in a
        /// non-generic type).
        /// </summary>
        /// <param name="hidingMember">
        /// The hiding member.
        /// </param>
        /// <param name="representativeMember">
        /// The member that we consider to be hidden (must have exactly the same custom modifiers as the hiding member).
        /// Assumed to already be in hiddenBuilder.
        /// </param>
        /// <param name="hiddenBuilder">
        /// Will have all other members with the same signature (including custom modifiers) as 
        /// representativeMember added.
        /// </param>
        private static void FindOtherHiddenMembersInContainingType(Symbol hidingMember, Symbol representativeMember, ref ArrayBuilder<Symbol> hiddenBuilder)
        {
            Debug.Assert((object)representativeMember != null);
            Debug.Assert(representativeMember.Kind != SymbolKind.Field);
            Debug.Assert(representativeMember.Kind != SymbolKind.NamedType);
            Debug.Assert(representativeMember.Kind == SymbolKind.Property || !representativeMember.ContainingType.IsDefinition);

            IEqualityComparer<Symbol> comparer = MemberSignatureComparer.CSharpCustomModifierOverrideComparer;
            foreach (Symbol otherMember in representativeMember.ContainingType.GetMembers(representativeMember.Name))
            {
                if (otherMember.Kind == representativeMember.Kind)
                {
                    if (otherMember != representativeMember && comparer.Equals(otherMember, representativeMember))
                    {
                        AddHiddenMemberIfApplicable(ref hiddenBuilder, hidingMember, otherMember);
                    }
                }
            }
        }

        private static bool CanOverrideOrHide(Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Property:
                case SymbolKind.Event:
                    // Explicit interface impls don't override or hide.
                    return !member.IsExplicitInterfaceImplementation();
                case SymbolKind.Method:
                    MethodSymbol methodSymbol = (MethodSymbol)member;
                    return MethodSymbol.CanOverrideOrHide(methodSymbol.MethodKind) && ReferenceEquals(methodSymbol, methodSymbol.ConstructedFrom);
                default:
                    throw ExceptionUtilities.UnexpectedValue(member.Kind);
            }
        }

        private static bool TypeOrReturnTypeHasCustomModifiers(Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    MethodSymbol method = (MethodSymbol)member;
                    var methodReturnType = method.ReturnTypeWithAnnotations;
                    return methodReturnType.CustomModifiers.Any() || method.RefCustomModifiers.Any() ||
                           methodReturnType.Type.HasCustomModifiers(flagNonDefaultArraySizesOrLowerBounds: false);
                case SymbolKind.Property:
                    PropertySymbol property = (PropertySymbol)member;
                    var propertyType = property.TypeWithAnnotations;
                    return propertyType.CustomModifiers.Any() || property.RefCustomModifiers.Any() ||
                           propertyType.Type.HasCustomModifiers(flagNonDefaultArraySizesOrLowerBounds: false);
                case SymbolKind.Event:
                    EventSymbol @event = (EventSymbol)member;
                    return @event.Type.HasCustomModifiers(flagNonDefaultArraySizesOrLowerBounds: false); //can't have custom modifiers on (vs in) type
                default:
                    throw ExceptionUtilities.UnexpectedValue(member.Kind);
            }
        }

        private static int CustomModifierCount(Symbol member)
        {
            switch (member.Kind)
            {
                case SymbolKind.Method:
                    MethodSymbol method = (MethodSymbol)member;
                    return method.CustomModifierCount();
                case SymbolKind.Property:
                    PropertySymbol property = (PropertySymbol)member;
                    return property.CustomModifierCount();
                case SymbolKind.Event:
                    EventSymbol @event = (EventSymbol)member;
                    return @event.Type.CustomModifierCount();
                default:
                    throw ExceptionUtilities.UnexpectedValue(member.Kind);
            }
        }

        /// <summary>
        /// Determine if this method requires a methodimpl table entry to inform the runtime of the override relationship.
        /// </summary>
        /// <param name="warnAmbiguous">True if we should produce an ambiguity warning per https://github.com/dotnet/roslyn/issues/45453 .</param>
        internal static bool RequiresExplicitOverride(this MethodSymbol method, out bool warnAmbiguous)
        {
            warnAmbiguous = false;
            if (!method.IsOverride)
                return false;

            MethodSymbol csharpOverriddenMethod = method.OverriddenMethod;
            if (csharpOverriddenMethod is null)
                return false;

            MethodSymbol runtimeOverriddenMethod = method.GetFirstRuntimeOverriddenMethodIgnoringNewSlot(out bool wasAmbiguous);
            if (csharpOverriddenMethod == runtimeOverriddenMethod && !wasAmbiguous)
                return false;

            // See https://github.com/dotnet/roslyn/issues/45453. No need to warn when the runtime
            // supports covariant returns because any methodimpl we produce to identify the specific
            // overridden method is unambiguously understood by the runtime.
            if (method.ContainingAssembly.RuntimeSupportsCovariantReturnsOfClasses)
                return true;

            // If the method was declared as a covariant return, there will be a compile-time error since the runtime
            // does not support covariant returns. In this case we do not warn about runtime ambiguity and pretend that
            // we can use a methodimpl (even though it is of a form not supported by the runtime and would result in a
            // loader error) so that the symbol APIs produce the most useful result.
            if (!method.ReturnType.Equals(csharpOverriddenMethod.ReturnType, TypeCompareKind.AllIgnoreOptions))
                return true;

            // Due to https://github.com/dotnet/runtime/issues/38119 the methodimpl would
            // appear to the runtime to be ambiguous in some cases.
            bool methodimplWouldBeAmbiguous = csharpOverriddenMethod.MethodHasRuntimeCollision();
            if (!methodimplWouldBeAmbiguous)
                return true;

            Debug.Assert(runtimeOverriddenMethod is { });

            // We produce the warning when a methodimpl would be required but would be ambiguous to the runtime.
            // However, if there was a duplicate definition for the runtime signature of the overridden
            // method where it was originally declared, that would have been an error.  In that case we suppress
            // the warning as a cascaded diagnostic.
            bool originalOverriddenMethodWasAmbiguous =
                csharpOverriddenMethod.IsDefinition || csharpOverriddenMethod.OriginalDefinition.MethodHasRuntimeCollision();
            warnAmbiguous = !originalOverriddenMethodWasAmbiguous;

            bool overriddenMethodContainedInSameTypeAsRuntimeOverriddenMethod =
                csharpOverriddenMethod.ContainingType.Equals(runtimeOverriddenMethod.ContainingType, TypeCompareKind.CLRSignatureCompareOptions);

            // If the overridden method is on a different (e.g. base) type compared to the runtime overridden
            // method, then the runtime overridden method could not possibly resolve correctly to the overridden method.
            // In this case we might as well produce a methodimpl. At least it has a chance of being correctly resolved
            // by the runtime, where the runtime resolution without the methodimpl would definitely be wrong.
            if (!overriddenMethodContainedInSameTypeAsRuntimeOverriddenMethod)
                return true;

            // This is the historical test, preserved since the days of the native compiler in case it turns out to affect compatibility.
            // However, this test cannot be true in a program free of errors.
            return csharpOverriddenMethod != runtimeOverriddenMethod && method.IsAccessor() != runtimeOverriddenMethod.IsAccessor();
        }

        internal static bool MethodHasRuntimeCollision(this MethodSymbol method)
        {
            foreach (Symbol otherMethod in method.ContainingType.GetMembers(method.Name))
            {
                if (otherMethod != method && MemberSignatureComparer.RuntimeSignatureComparer.Equals(otherMethod, method))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Given a method, find the first method that it overrides from the perspective of the CLI.
        /// Key differences from C#: non-virtual methods are ignored, the RuntimeSignatureComparer
        /// is used (i.e. consider return types, ignore ref/out distinction).  Sets <paramref name="wasAmbiguous"/>
        /// to true if more than one method is overridden by CLI rules.
        /// </summary>
        /// <remarks>
        /// WARN: Must not check method.MethodKind - PEMethodSymbol.ComputeMethodKind uses this method.
        /// NOTE: Does not check whether the given method will be marked "newslot" in metadata (as
        /// "newslot" is used for covariant method overrides).
        /// </remarks>
        internal static MethodSymbol GetFirstRuntimeOverriddenMethodIgnoringNewSlot(this MethodSymbol method, out bool wasAmbiguous)
        {
            // WARN: If the method may override a source method and declaration diagnostics have yet to
            // be computed, then it is important for us to pass ignoreInterfaceImplementationChanges: true
            // (see MethodSymbol.IsMetadataVirtual for details).
            // Since we are only concerned with overrides (of class methods), interface implementations can be ignored.
            const MethodSymbol.IsMetadataVirtualOption ignoreInterfaceImplementationChanges = MethodSymbol.IsMetadataVirtualOption.IgnoreInterfaceImplementationChanges;

            wasAmbiguous = false;
            if (!method.IsMetadataVirtual(ignoreInterfaceImplementationChanges) || method.IsStatic)
            {
                return null;
            }

            NamedTypeSymbol containingType = method.ContainingType;

            for (NamedTypeSymbol currType = containingType.BaseTypeNoUseSiteDiagnostics; !ReferenceEquals(currType, null); currType = currType.BaseTypeNoUseSiteDiagnostics)
            {
                MethodSymbol candidate = null;
                foreach (Symbol otherMember in currType.GetMembers(method.Name))
                {
                    if (otherMember.Kind == SymbolKind.Method &&
                        IsOverriddenSymbolAccessible(otherMember, containingType) &&
                        MemberSignatureComparer.RuntimeSignatureComparer.Equals(method, otherMember))
                    {
                        MethodSymbol overridden = (MethodSymbol)otherMember;

                        // NOTE: The runtime doesn't consider non-virtual methods during override resolution.
                        if (overridden.IsMetadataVirtual(ignoreInterfaceImplementationChanges))
                        {
                            if (candidate is { })
                            {
                                // found more than one possible override in this type
                                wasAmbiguous = true;
                                return candidate;
                            }

                            candidate = overridden;
                        }
                    }
                }

                if (candidate is { })
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <remarks>
        /// Note that the access check is done using the original definitions.  This is because we want to avoid
        /// reductions in accessibility that result from type argument substitution (e.g. if an inaccessible type
        /// has been passed as a type argument).
        /// See DevDiv #11967 for an example.
        /// </remarks>
        private static bool IsOverriddenSymbolAccessible(Symbol overridden, NamedTypeSymbol overridingContainingType)
        {
            var discardedUseSiteInfo = CompoundUseSiteInfo<AssemblySymbol>.Discarded;
            return AccessCheck.IsSymbolAccessible(overridden.OriginalDefinition, overridingContainingType.OriginalDefinition, ref discardedUseSiteInfo);
        }
    }
}
