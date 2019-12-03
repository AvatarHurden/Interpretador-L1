﻿module TypeDeclarationTests

open NUnit.Framework
open FsUnit
open TestHelpers
open Parser
open Definition
open Evaluation
open TypeInference
open Translation

let throwsWrongType text =
    let parsed = text |> parsePure |> flip translate stdlib.stdEnv
    (fun () -> typeInfer parsed |> ignore) |> should throw typeof<TypeException>

let throwsParseException text =
    (fun () -> text |> parsePure |> flip translate stdlib.stdEnv |> ignore) 
        |> should throw typeof<ParseException>

let matchesType text typ =
    let parsed = text |> parsePure |> flip translate stdlib.stdEnv
    let typ' = typeInfer <| parsed
    let freeVars = List.sort <| getFreeVars typ defaultEnv |> List.unzip |> fst
    let freeVars' = List.sort <| getFreeVars typ' defaultEnv  |> List.unzip |> fst
    let freePairs = List.zip freeVars freeVars'
    let replaced = List.fold (fun acc (x, x') -> substituteInType (NameSub (x', x)) acc)
                        typ' freePairs
    typ |> should equal replaced
   
let hasType text typ =
    let parsed = text |> parsePure |> flip translate stdlib.stdEnv
    let typ' = typeInfer <| parsed
    typ' |> should equal typ

let equals text term =
    let parsed = text |> parsePure |> flip translate stdlib.stdEnv
    let typ = typeInfer <| parsed
    let evaluated = evaluate <| parsed
    evaluated |> should equal term

let equalsParsed text text' =
    let parsed = text |> parsePure |> flip translate stdlib.stdEnv
    let parsed' = text' |> parsePure |> flip translate stdlib.stdEnv
    let typ = typeInfer <| parsed
    let typ' = typeInfer <| parsed'
    let evaluated = evaluate <| parsed
    let evaluated' = evaluate <| parsed'
    evaluated |> should equal evaluated'

[<TestFixture>]
 type SimpleEnumDeclaration() =

    [<Test>]
    member this.allowsSimpleDeclaration() =
        equals "type Data = One; One" <| ResConstructor (Custom "One", [])

    [<Test>]
    member this.constructorHasCorrectType() =
        hasType "type Data = One; One" <| ConstType (CustomType "Data", [])

    [<Test>]
    member this.recognizesExplicitType() =
        equals "type Data = One;
                 let x: Data = One;
                 x" <| ResConstructor (Custom "One", [])

    [<Test>]
    member this.mismatchedTypeErrors() =
        throwsWrongType "type Data = One; let x: Data = 1; x"
    
    [<Test>]
    member this.requiresUpperCaseName() =
        throwsParseException "type data = One; 4"

    [<Test>]
    member this.requiresUpperCaseConstructor() =
        throwsParseException "type Data = one; 4"

    [<Test>]
    member this.requiresDeclaration() =
        throwsWrongType "type Data = One; Two"

    [<Test>]
    member this.repeatedConstructorCausesError() =
        throwsParseException "type Data = One | One; One"

    [<Test>]
    member this.repeatedConstructorShadows() =
        hasType "type Data = One | Two; type Data2 = One | Three; One"
            <| ConstType (CustomType "Data2", [])

    [<Test>]
    member this.typeIsStoredForVariable() =
        hasType "type Data = One | Two; let x = One; type Data2 = One | Three; x"
            <| ConstType (CustomType "Data", [])
    
    [<Test>]
    member this.allowsDeclarationWithMultipleValues() =
        equals "type Data = One | Two | Three; One" <| ResConstructor (Custom "One", [])

    [<Test>]
    member this.allowsOptionalLeadingPipe() =
        equals "type Data = 
                    | One 
                    | Two 
                    | Three; 
                One" <| ResConstructor (Custom "One", [])

    [<Test>]
    member this.matchExpressionWorksForSimpleDeclaration() =
        equals "type Data = One | Two | Three;
                let x = Two;
                match x with
                    | One -> 1
                    | Two -> 2
                    | Three -> 3" <| ResConstructor (I 2, [])

    [<Test>]
    member this.matchExpressionRequiresRightType() =
        throwsWrongType 
               "type Data = One | Two | Three;
                type Data2 = Banana;
                let x = Two;
                match x with
                    | One -> 1
                    | Two -> 2
                    | Banana -> 3"

    [<Test>]
    member this.letDeconstructionWorks() =
        equals "type Data = One | Two | Three;
                let x = One;
                let One = x;
                x" <| ResConstructor (Custom "One", [])

    [<Test>]
    member this.letDeconstructionRaisesForWrongConstructor() =
        equals "type Data = One | Two | Three;
                let x = One;
                let Two = x;
                x" <| ResRaise
    
    [<Test>]
    member this.letDeconstructionFailsForWrongType() =
        throwsWrongType 
               "type Data = One | Two | Three;
                let x = One;
                let False = x;
                x"
    
    [<Test>]
    member this.matchExpressionFailsWithShadowing() =
        throwsWrongType 
               "type Data = One | Two | Three;
                let x = Two;
                type Data2 = One;
                match x with
                    | One -> 1
                    | Two -> 2
                    | Three -> 3"

[<TestFixture>]
 type CompoundTypeDeclaration() =

    [<Test>]
    member this.allowsSingleDeclaration() =
        equals "type Shape = Circle Int; Circle 4" <| ResConstructor (Custom "Circle", [ResConstructor (I 4, [])])

    [<Test>]
    member this.constructorHasCorrectType() =
        hasType "type Shape = Circle Int; Circle 4" <| ConstType (CustomType "Shape", [])

    [<Test>]
    member this.failsOnWrongParameter() =
        throwsWrongType "type Shape = Circle Int; Circle 'a'"

    [<Test>]
    member this.recognizesExplicitType() =
        equals "type Shape = Circle Int;
                 let x: Shape = Circle 4;
                 x" <| ResConstructor (Custom "Circle", [ResConstructor (I 4, [])])
    
    [<Test>]
    member this.allowsMultipleDeclaration() =
        equals "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                 Rectangle 4 5" <| ResConstructor (Custom "Rectangle", [ResConstructor (I 4, []); ResConstructor (I 5, [])])
    
    [<Test>]
    member this.matchExpressionWorksForCompoundDeclaration() =
        equals "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Rectangle 5 6;
                match x with
                    | Circle r -> 6 * r
                    | Rectangle w h -> w * h" <| ResConstructor (I 30, [])

    [<Test>]
    member this.matchExpressionFailsWithMoreArguments() =
        throwsWrongType 
               "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Rectangle 5 6;
                match x with
                    | Circle r d -> 6 * r
                    | Rectangle w h -> w * h"
     
    [<Test>]
    member this.matchExpressionFailsWithLessArguments() =
        throwsWrongType 
               "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Rectangle 5 6;
                match x with
                    | Circle r -> 6 * r
                    | Rectangle w -> w * 4"
    
    [<Test>]
    member this.letDeconstructionWorks() =
        equals "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Rectangle 5 6;
                let Rectangle w d = x;
                w * d" <| ResConstructor (I 30, [])
    
    [<Test>]
    member this.letDeconstructionFailsWithLessArguments() =
        throwsWrongType 
               "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Rectangle 5 6;
                let Rectangle w = x;
                w"

    [<Test>]
    member this.letDeconstructionFailsWithMoreArguments() =
        throwsWrongType 
               "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Circle 4;
                let Circle w d = x;
                w * d"

    [<Test>]
    member this.letDeconstructionRaisesWithWrongConstructor() =
        equals "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Circle 4;
                let Rectangle w d = x;
                w * d" <| ResRaise

    [<Test>]
    member this.letDeconstructionFailsWithWrongType() =
        throwsWrongType
               "type Shape = 
                    | Circle Int
                    | Rectangle Int Int;
                let x = Circle 4;
                let False = x;
                4"

[<TestFixture>]
 type GenericTypeDeclaration() =

    [<Test>]
    member this.allowsSingleDeclaration() =
        equals "type Optional a = Some a; Some 4" <| ResConstructor (Custom "Some", [ResConstructor (I 4, [])])

    [<Test>]
    member this.constructorHasCorrectType() =
        hasType "type Optional a = Some a; Some 4" <| ConstType (CustomType "Optional", [Int'])

    [<Test>]
    member this.failsOnUndeclaredVarType() =
        throwsWrongType "type Optional a = Some b; Some 4"
    
    [<Test>]
    member this.recognizesExplicitType() =
        equals "type Optional a = Some a;
                 let x: Optional Int = Some 4;
                 x" <| ResConstructor (Custom "Some", [ResConstructor (I 4, [])])

    [<Test>]
    member this.failsOnWrongExplicitGeneric() =
        throwsWrongType 
                "type Optional a = Some a;
                 let x: Optional Char = Some 4;
                 x"
    
    [<Test>]
    member this.recognizesExplicitGenericType() =
        equals "type Optional a = Some a;
                 let x: Optional a = Some 4;
                 x" <| ResConstructor (Custom "Some", [ResConstructor (I 4, [])])

    [<Test>]
    member this.explicitGenericTypeStillHasConcreteType() =
        matchesType 
                "type Optional a = Some a;
                 let x: Optional a = Some 4;
                 x" <| ConstType (CustomType "Optional", [Int'])

    [<Test>]
    member this.allowsMultipleDeclaration() =
        equals "type Optional a = 
                    | None
                    | Some a;
                 Some 4" <| ResConstructor (Custom "Some", [ResConstructor (I 4, [])])
    
    [<Test>]
    member this.passesGenericType() =
        matchesType "type Optional a = 
                    | None
                    | Some a;
                 None" <| ConstType (CustomType "Optional", [VarType ("a", [])])

    [<Test>]
    member this.allowsConcreteTypes() =
        equals "type Optional a = Some a | Other Int; Other 4" 
            <| ResConstructor (Custom "Other", [ResConstructor (I 4, [])])

    [<Test>]
    member this.matchExpressionWorksForCompoundDeclaration() =
        equals "type Optional a = 
                    | None
                    | Some a;
                let x = Some 5;
                match x with
                    | None -> 0
                    | Some y -> y" <| ResConstructor (I 5, [])

    [<Test>]
    member this.matchExpressionFailsWithMoreArguments() =
        throwsWrongType 
               "type Optional a = 
                    | None
                    | Some a;
                let x = Some 5;
                match x with
                    | None y -> y
                    | Some y -> y"
     
    [<Test>]
    member this.matchExpressionFailsWithLessArguments() =
        throwsWrongType 
               "type Optional a = 
                    | None
                    | Some a;
                let x = Some 5;
                match x with
                    | None -> 0
                    | Some -> 1"
    
    [<Test>]
    member this.letDeconstructionWorks() =
        equals "type Optional a = 
                    | None
                    | Some a;
                let x = Some 5;
                let Some y = x;
                y" <| ResConstructor (I 5, [])
    
    [<Test>]
    member this.letDeconstructionFailsWithLessArguments() =
        throwsWrongType 
               "type Optional a = 
                    | None
                    | Some a;
                let x = Some 5;
                let Some = x;
                4"

    [<Test>]
    member this.letDeconstructionFailsWithMoreArguments() =
        throwsWrongType 
               "type Optional a = 
                    | None
                    | Some a;
                let x = None;
                let None y = x;
                y"

    [<Test>]
    member this.letDeconstructionRaisesWithWrongConstructor() =
        equals "type Optional a = 
                    | None
                    | Some a;
                let x = None;
                let Some y = x;
                y" <| ResRaise

    [<Test>]
    member this.letDeconstructionFailsWithWrongType() =
        throwsWrongType
               "type Optional a = 
                    | None
                    | Some a;
                let x = None;
                let False = x;
                4"