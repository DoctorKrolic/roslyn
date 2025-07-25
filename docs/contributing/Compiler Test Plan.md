This document provides guidance for thinking about language interactions and testing compiler changes.

# General concerns:
- Completeness of the specification as a guide for testing (is the spec complete enough to suggest what the compiler should do in each scenario?)
- *Ping* for new breaking changes and general ping for partner teams (Bill, Kathleen, Mads, IDE, Razor)
- Help review external documentation
- Backward and forward compatibility (interoperation with previous and future compilers, each in both directions)
- Error handling/recovery (missing libraries, including missing types in mscorlib; errors in parsing, ambiguous lookup, inaccessible lookup, wrong kind of thing found, instance vs static thing found, wrong type for the context, value vs variable)
- BCL (including mono) and other customer impact
- Determinism
- Loading from metadata (source vs. loaded from metadata)
- Public interface of compiler APIs (including semantic model APIs listed below):
    - GetDeclaredSymbol 
    - GetEnclosingSymbol 
    - GetSymbolInfo 
    - GetSpeculativeSymbolInfo 
    - GetTypeInfo 
    - GetSpeculativeTypeInfo 
    - GetMethodGroup 
    - GetConstantValue 
    - GetAliasInfo 
    - GetSpeculativeAliasInfo 
    - LookupSymbols 
    - AnalyzeStatementsControlFlow 
    - AnalyzeStatementControlFlow 
    - AnalyzeExpressionDataFlow 
    - AnalyzeStatementsDataFlow 
    - AnalyzeStatementDataFlow 
    - ClassifyConversion
    - GetOperation (`IOperation`)
    - GetCFG (`ControlFlowGraph`)
- VB/F# interop
- C++/CLI interop (particularly for metadata format changes, e.g. DIMs, static abstracts in interfaces, or generic attributes)
- Performance and stress testing
- Can build VS
- Check that `Obsolete` is honored for members used in binding/lowering
- LangVersion
 
# Type and members
- Access modifiers (public, protected, internal, protected internal, private protected, private), static, ref
- type declarations
  - class with or without primary constructor
  - record class/struct with or without positional members
  - struct with or without primary constructor
  - interface
  - type parameter
- file-local types
- methods
  - Primary constructors
- fields (required and not)
- properties (including get/set/init accessors, required and not)
- events (including add/remove accessors)
- Parameter modifiers: ref, out, in, ref readonly, params (for array, for non-array)
- Attributes (including generic attributes and security attributes)
  - Compiler-recognized attributes should not have any effect in earlier LangVersions,
    except a LangVersion error should be reported when functionality depending on the attribute is used
    (for example, InlineArray conversion to Span).
- Generics (type arguments, variance, constraints including `class`, `struct`, `new()`, `unmanaged`, `notnull`, `allows ref struct`, types and interfaces with nullability)
- Default and constant values
- Partial classes
- Literals
- Enum (implicit vs. explicit underlying type)
- Expression trees
- Iterators
- Initializers (object, collection, dictionary)
- Array (single- or multi-dimensional, jagged, initializer, fixed)
- Expression-bodied methods/properties/...
- Extension methods
- Partial method
- Named and optional parameters
- String interpolation
- Raw strings (including interpolation)
- Properties (read-write, read-only, init-only, write-only, auto-property, expression-bodied)
- Interfaces (implicit vs. explicit interface member implementation)
- Delegates
- Multi-declaration
- NoPIA
- Dynamic
- Ref structs, Readonly structs
- Readonly members on structs (methods, property/indexer accessors, custom event accessors)
- SkipLocalsInit
- Method override or explicit implementation with `where T : { class, struct, default }`
- `extension` blocks (emitted with content-based names)
 
# Code
- Operators (see Eric's list below)
- Lambdas (capture of parameters or locals, target typing)
- Execution order
- Target typing (var, lambdas, integrals)
- Conversions (boxing/unboxing)
- Nullable (wrapping, unwrapping)
- Overload resolution, override/hide/implement (OHI)
  - OverloadResolutionPriorityAttribute
- Inheritance (virtual, override, abstract, new)
- Anonymous types
- Tuple types and literals (elements with explicit or inferred names, long tuples), tuple equality
- Range literals (`1..2`) and Index operator (`^1`) 
- Deconstructions
- Local functions
- Unsafe code
- LINQ
- Constructors, properties, indexers, events, operators, and destructors.
- Async (task-like types) and async-iterator methods
- Lvalues: the synthesized fields are mutable 
    - Ref / out parameters
    - Compound operators (`+=`, `/=`, etc ..) 
    - Assignment exprs
- Ref return, ref readonly return, ref ternary, ref readonly local, ref local re-assignment, ref foreach
- Ref fields
- `scoped` parameters and locals
- `this = e;` in `struct` .ctor
- Stackalloc (including initializers)
- Patterns (constant, declaration, `var`, positional, property and extended property, discard, parenthesized, type, relational, `and`/`or`/`not`, list, slice, constant `string` matching `Span<char>`)
- Switch expressions
- With expressions (on record classes and on value types)
- Nullability annotations (`?`, attributes) and analysis
- Definite assignment analysis and auto-default struct fields
- If you add a place an expression can appear in code, make sure `SpillSequenceSpiller` handles it. Test with a `switch` expression or `stackalloc` in that place.
- If you add a new expression form that requires spilling, test it in the catch filter.
- If you add a new expression that can be nested inside itself, test deeply nesting it. Potentially cut off direct nesting.
- extension based Dispose, DisposeAsync, GetEnumerator, GetAsyncEnumerator, Deconstruct, GetAwaiter etc.
- UTF8 String Literals (string literals with 'u8' or 'U8' type suffix).
- Inline array element access and slicing.
- Collection expressions and spread elements

# Misc
- reserved keywords (sometimes contextual)
- pre-processing directives
- COM interop
- modopt and modreq
- CompilerFeatureRequiredAttribute
- ref assemblies
- extern alias
- UnmanagedCallersOnly
- telemetry

# Testing in interaction with other components
Interaction with IDE, Debugger, and EnC should be worked out with relevant teams. A few highlights:
- IDE
    - Colorization and formatting
    - Typing experience and dealing with incomplete code
    - Intellisense (squiggles, dot completion)
    - "go to", Find All References, and renaming
    - F1 help (for new keywords)
    - cref comments
    - UpgradeProject code fixer
    - More: [IDE Test Plan](https://github.com/dotnet/roslyn/blob/main/docs/contributing/IDE%20Test%20Plan.md)

- Debugger / EE
    - Stepping, setting breakpoints
    - Displaying Locals/Autos windows
        - Type and Value display
        - Expanding instance members
        - Locals/parameters/this in closure classes
        - Attributes on locals and expressions (e.g.: [Dynamic], [TupleElementNames])
    - Compiling expressions in Immediate/Watch windows or hovering over an expression
    - Compiling expressions in [DebuggerDisplay("...")]
	- Assigning values in Locals/Autos/Watch windows

- [Edit-and-continue](https://github.com/dotnet/roslyn/blob/main/docs/contributing/Testing%20for%20Interactive%20readiness.md)

- Live Unit Testing (instrumentation)

- Engage with VS Templates team (if applicable)

# Eric's cheatsheet

## Statements 
```
{ … }  
;   
label : … 
T x = whatever; // including `using` and `await` using variants
M(); 
++x; 
x++; 
--x; 
x--;
a?.M(); // conditional access / null-propagating operator
a?[b] = c;
a?.b = c;
a?[b] = c;
a?.b += c; // and other compound assignment cases
var x = a?.M(); // similar "value is used" versions of the above '?.' cases
new C(); 
if (…) … else … 
switch(…) { … case (…) when (…): … } 
while(…) … 
do … while(…); 
for( … ; … ; … ) … 
foreach(…) … // including `await` variant
fixed(…) … // (plain, or custom with `GetPinnableReference`)
goto … ; 
throw … ; 
return … ; 
try  { … } catch (…) when (…) { … } finally { … } 
checked { … } 
unchecked { … } 
lock(…) … // including variant on an instance of the `System.Threading.Lock` type
using (…) … // including `await` variant
yield return …; 
yield break; 
break; 
continue; 
```

## Expression classifications 
  
Every expression can be classified as exactly one of these: 
  
- Value 
- Variable 
- Namespace 
- Type 
- Method group 
- Null literal
- Default literal
- Anonymous function 
- Property 
- Indexer 
- Event 
- Void-returning method call 
- Array initializer (\*) 
- __arglist (\*)  

(\*) Technically not an expression according to the spec. 
  
Note that only values, variables, properties, indexers and events have a type. 
  
## Variable classifications 
  
A variable is a storage location. These are all the different ways to refer to a storage location: 

- Static field 
- Instance field 
- Array element 
- Formal param, value 
- Formal param, ref 
- Formal param, out 
- Local variable 
- Pointer dereference 
- __refvalue
- Inline array access

## Operators 

``` c#
x.y 
f( ) 
a[e] 
x++ (including instance user defined)
x-- (including instance user defined)
new X() 
new() 
typeof(T) 
default(T)
default 
checked(e) 
unchecked(e) 
delegate ( ) { } 
+x 
-x 
!x 
~x 
^x
++x (including instance user defined)
--x (including instance user defined)
(X)x 
x * y 
x / y 
x % y 
x + y 
x - y 
x << y 
x >> y 
x >>> y 
x < y 
x > y 
x <= y 
x >= y 
x is X 
x as X 
x == y 
x != y 
x & y 
x ^ y 
x | y 
x && y 
x || y 
x ?? y 
x ? : y : z
x = y 
x *= y (including instance user defined)
x /= y (including instance user defined)
x %= y (including instance user defined)
x += y (including instance user defined)
x -= y (including instance user defined)
x <<= y (including instance user defined)
x >>= y (including instance user defined)
x >>>= y (including instance user defined)
x &= y (including instance user defined)
x ^= y (including instance user defined)
x |= y (including instance user defined)
x ??= y
x => { } 
sizeof( ) 
*x 
& x 
x->y 
e is pattern
e switch { ... }
await x
__arglist( ) 
__refvalue( x, X ) 
__reftype( x )
__makeref( x )
```

## Explicit conversions 
  
- Numeric 
- Enum 
- Nullable 
- Reference  
- Unboxing 
- Dynamic 
- Type parameter 
- User defined 
- Pointer to pointer 
- Pointer to integral 
- Integral to pointer 
- Tuple literal
- Tuple

## Implicit conversions 
  
- Identity 
- Numeric 
- Literal zero to enum (we actually do constant zero, not literal zero) 
- Nullable 
- Null literal 
- Reference 
- Boxing 
- Dynamic 
- Constant 
- Type parameter 
- User defined 
- Anonymous function 
- Method group  
- Pointer to void pointer 
- Null literal to pointer
- Interpolated string
- Tuple literal
- Tuple
- Default literal
- Implicit object creation (target-typed new)
- Function type (in type inference comparing function types of lambdas or method groups)
- UTF8 String Literal (string constant value to ```byte[]```, ```Span<byte>```, or ```ReadOnlySpan<byte>``` types)
- Inline arrays (conversions to Span and ReadOnlySpan)
- Collection expression conversions
- Span conversions (from array to (ReadOnly)Span, or from string or (ReadOnly)Span to ReadOnlySpan)

## Types 

- Class
- Interface 
- Delegate 
- Struct 
- Enum
- Nullable 
- Pointer
- Type parameter

## Members

- Class
- Struct
- Interface 
- Enum
- Delegate
- Namespace 
- Property 
- Event
- Constructor 
- Destructor 
- Method
- Interface method 
- Field
- User-defined indexer
- User-defined operator (including checked, including instance increment/decrement and compound assignment)
- User-defined conversion (including checked)

## Patterns
- Discard Pattern
- Var Pattern
- Declaration Pattern
- Constant Pattern
- Constant `string` matching `Span<char>`
- Recursive Pattern
- Parenthesized Pattern
- `and` Pattern
- `or` Pattern
- `not` Pattern
- Relational Pattern
- Type Pattern

## Metadata table numbers / token prefixes 
 
If you look at a 32 bit integer token as a hex number, the first two digits identify the “table number” and the last six digits are an offset into that table. The table numbers are: 
```
00 Module 
01 TypeRef 
02 TypeDef 
03 FieldPtr 
04 Field 
05 MethodPtr 
06 Method 
07 ParamPtr 
08 Param 
09 InterfaceImpl 
0A MemberRef 
0B Constant 
0C CustomAttr 
0D FieldMarshal 
0E DeclSecurity 
0F ClassLayout 
10 FieldLayout 
11 StandAloneSig 
12 EventMap 
13 EventPtr 
14 Event 
15 PropertyMap 
16 PropertyPtr 
17 Property 
18 MethodSemantics 
19 MethodImpl 
1A ModuleRef 
1B TypeSpec 
1C ImplMap 
1D FieldRVA 
1E ENCLog 
1F ENCMap 
20 Assembly 
21 AssemblyProcessor 
22 AssemblyOS 
23 AssemblyRef 
24 AssemblyRefProcessor 
25 AssemblyRefOS 
26 File 
27 ExportedType 
28 ManifestResource 
29 NestedClass 
2A GenericParam 
2B MethodSpec 
2C GenericConstraint 
```
