﻿module Evaluation

open Definition
open System

//#region String Conversion

let rec private toString term =
    match term with
    | ResCons (ResC c, t2) -> (string c) + (toString t2)
    | t -> "" 

let rec private fromString string =
    match string with
    | c::rest -> ResCons (ResC c, fromString rest)
    | [] -> ResNil

//#endregion

//#region Pattern Matching

let rec matchPattern (Var (pattern, _)) result (env: Map<Ident, result>) =
    match pattern with
    | XPattern x -> Some <| env.Add(x, result)
    | IgnorePattern -> Some env  
    | TuplePattern patterns ->
        match result with
        | ResTuple results when results.Length = patterns.Length ->
            let f acc p r =
                match acc with
                | None -> None
                | Some env -> matchPattern p r env
            List.fold2 f (Some env) patterns results
        | ResTuple results ->
            raise <| EvalException "Tuples do not match in pattern"
        | _ -> 
            raise <| EvalException "Invalid result for tuple pattern"
    | RecordPattern patterns ->
        match result with
        | ResRecord results when results.Length = patterns.Length ->

            let existsInPatterns (rName, _) =
                List.exists (fun (pName, _) -> pName = rName) patterns

            if List.forall existsInPatterns results then
                let f acc (pName, pValue) =
                    match acc with
                    | None -> None
                    | Some env -> 
                        let (_, rValue) = List.find (fun (rName, rValue) -> rName = pName) results
                        matchPattern pValue rValue env
                List.fold f (Some env) patterns
            else
                raise <| EvalException "Records have different fields in pattern"
        | ResRecord results ->
            raise <| EvalException "Records have different lengths in pattern"
        | _ -> 
            raise <| EvalException "Invalid result for record pattern"
    | NilPattern ->
        match result with
        | ResNil -> Some env
        | ResCons _ -> None
        | _ -> 
            raise <| EvalException "Invalid result for nil pattern"
    | ConsPattern (p1, p2) ->
        match result with
        | ResNil -> None
        | ResCons (v1, v2) -> 
            match matchPattern p1 v1 env with
            | None -> None
            | Some env -> matchPattern p2 v2 env
        | _ -> 
            raise <| EvalException "Invalid result for cons pattern"

let validatePattern pattern result (env: Map<Ident, result>) =
    matchPattern pattern result env
        
//#endregion

//#region Comparisons

let rec compareEquality t1 t2 =
    match t1, t2 with
    | ResRaise, _ -> ResRaise
    | _, ResRaise -> ResRaise
    | ResI i1, ResI i2 -> ResB (i1 = i2)
    | ResC c1, ResC c2 -> ResB (c1 = c2)
    | ResB b1, ResB b2 -> ResB (b1 = b2)
    | ResNil, ResNil -> ResB true
    | ResCons (hd1, tl1), ResNil -> ResB false
    | ResNil, ResCons (hd1, tl1)  -> ResB false
    | ResCons (hd1, tl1), ResCons (hd2, tl2) ->
        match compareEquality hd1 hd2, compareEquality tl1 tl2 with
        | ResB false, _ -> ResB false
        | ResB true, ResB false -> ResB false
        | ResB true, ResB true -> ResB true
        | _ -> raise <| EvalException "Equal returned a non-expected value"
    | ResTuple v1, ResTuple v2 when v1.Length = v2.Length ->
        let f acc r1 r2 =
            match acc, compareEquality r1 r2 with
            | ResRaise, _
            | _, ResRaise -> ResRaise
            | ResB b1, ResB b2 -> ResB (b1 && b2)
            | _ -> raise <| EvalException "Equal returned a non-expected value"
        List.fold2 f (ResB true) v1 v2
    | ResRecord v1, ResRecord v2 when v1.Length = v2.Length ->
        let existsInV1 (name2, _) =
            List.exists (fun (name1, _) -> name2 = name1) v1
        
        if List.forall existsInV1 v2 then
            let f acc (name1, r1) =
                let (name2, r2) = List.find (fun (name2, typ2) -> name1 = name2) v2
                match acc, compareEquality r1 r2 with
                | ResRaise, _
                | _, ResRaise -> ResRaise
                | ResB b1, ResB b2 -> ResB (b1 && b2)
                | _ -> raise <| EvalException "Equal returned a non-expected value"
            List.fold f (ResB true) v1
        else
            raise <| EvalException (sprintf "Records %A and %A have different fields" t1 t2)
    | _ , _ -> sprintf "Values %A and %A are not comparable" t1 t2 |> EvalException |> raise  

let rec compareOrder t1 t2 orderType =
    match t1, t2 with
    | ResRaise, _ -> ResRaise
    | _, ResRaise -> ResRaise
    | ResI i1, ResI i2 -> 
        match orderType with
        | LessThan -> ResB (i1 < i2)
        | LessOrEqual -> ResB (i1 <= i2)
        | GreaterOrEqual -> ResB (i1 >= i2)
        | GreaterThan -> ResB (i1 > i2)
        | _ -> sprintf "Cannot order %A and %A with %A" t1 t2 orderType |> EvalException |> raise  
    | ResC c1, ResC c2 -> 
        match orderType with
        | LessThan -> ResB (c1 < c2)
        | LessOrEqual -> ResB (c1 <= c2)
        | GreaterOrEqual -> ResB (c1 >= c2)
        | GreaterThan -> ResB (c1 > c2)
        | _ -> sprintf "Cannot order %A and %A with %A" t1 t2 orderType |> EvalException |> raise  
    | ResNil, ResNil ->
        match orderType with
        | LessOrEqual | GreaterOrEqual -> ResB true
        | LessThan | GreaterThan -> ResB false
        | _ -> sprintf "Cannot order %A and %A with %A" t1 t2 orderType |> EvalException |> raise  
    | ResCons (hd1, tl1), ResNil ->
        match orderType with
        | GreaterOrEqual | GreaterThan -> ResB true
        | LessOrEqual | LessThan -> ResB false
        | _ -> sprintf "Cannot order %A and %A with %A" t1 t2 orderType |> EvalException |> raise  
    | ResNil, ResCons (hd1, tl1) ->
        match orderType with
        | LessOrEqual | LessThan -> ResB true
        | GreaterOrEqual | GreaterThan -> ResB false
        | _ -> sprintf "Cannot order %A and %A with %A" t1 t2 orderType |> EvalException |> raise  
    | ResCons (hd1, tl1), ResCons (hd2, tl2) ->
        match compareEquality hd1 hd2, compareOrder tl1 tl2 orderType with
        | ResB true, t2' -> t2'
        | ResB false, _ -> compareOrder hd1 hd2 orderType
        | _ -> raise <| EvalException "Equal returned a non-expected value"
    | _ , _ -> sprintf "Values %A and %A are not comparable" t1 t2 |> EvalException |> raise  
    
//#endregion

let rec private eval t env =
    match t with
    | B b-> ResB b
    | Skip -> ResSkip
    | I i -> ResI i
    | C c -> ResC c
    | OP(t1, Application, t2) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResRecClosure(id1, pattern, e, env') as t1' ->
            match eval t2 env with
            | ResRaise ->  ResRaise
            | t2' -> 
                match validatePattern pattern t2' env' with
                | None -> ResRaise
                | Some env' -> eval e <| env'.Add(id1, t1')
        | ResClosure(pattern, e, env') ->
            match eval t2 env with
            | ResRaise -> ResRaise
            | t2' -> 
                match validatePattern pattern t2' env' with
                | None -> ResRaise
                | Some env' -> eval e env'
        | t1' -> sprintf "First operand %A is not a function at %A" t1' t |> EvalException |> raise
    | OP(t1, Cons, t2) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | t1' ->
            match eval t2 env with
            | ResRaise -> ResRaise
            | ResCons(_, _) as t2' -> ResCons(t1', t2')
            | ResNil -> ResCons(t1', ResNil)
            | t2' -> sprintf "Term %A is not a list at %A" t2' t |> EvalException |> raise
    | OP(t1, Sequence, t2) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResSkip -> eval t2 env
        | t1' -> sprintf "First operand %A is not skip at %A" t1' t |> EvalException |> raise
    | OP(t1, Equal, t2) ->
        compareEquality (eval t1 env) (eval t2 env)
    | OP(t1, Different, t2) ->
        let equals = compareEquality (eval t1 env) (eval t2 env)
        match equals with
        | ResRaise -> ResRaise
        | ResB b -> ResB (not b)
        | _ -> raise <| EvalException "Equal returned a non-expected value"
    | OP(t1, (LessThan as op), t2)
    | OP(t1, (LessOrEqual as op), t2)
    | OP(t1, (GreaterOrEqual as op), t2)
    | OP(t1, (GreaterThan as op), t2) ->
        compareOrder (eval t1 env) (eval t2 env) op
    | OP(t1, (Add as op), t2)
    | OP(t1, (Subtract as op), t2)
    | OP(t1, (Multiply as op), t2)
    | OP(t1, (Divide as op), t2) ->
        match eval t1 env, eval t2 env with
        | ResRaise, _ -> ResRaise
        | _, ResRaise -> ResRaise
        | ResI i1, ResI i2 ->
            match op with
            | Add -> ResI (i1 + i2)
            | Subtract -> ResI (i1 - i2)
            | Multiply -> ResI (i1 * i2)
            | Divide when i2 <> 0 -> ResI (i1 / i2)
            | Divide when i2 = 0 -> ResRaise
            | _ -> sprintf "Term %A is not an operator at %A" op t |> EvalException |> raise
        | _, _ -> sprintf "Operation %A requires numbers at %A" op t |> EvalException |> raise
    | OP(t1, And, t2) ->
        match eval t1 env, eval t2 env with
        | ResRaise, _ -> ResRaise
        | ResB false, _ -> ResB false
        | ResB true, ResRaise -> ResRaise
        | ResB true, ResB true -> ResB true
        | ResB true, ResB false -> ResB false
        | t1', t2' -> sprintf "AND operation requires boolean values at %A" t |> EvalException |> raise
    | OP(t1, Or, t2) ->
        match eval t1 env, eval t2 env with
        | ResRaise, _ -> ResRaise
        | ResB true, _ -> ResB true
        | ResB false, ResRaise -> ResRaise
        | ResB false, ResB true -> ResB true
        | ResB false, ResB false -> ResB false
        | t1', t2' -> sprintf "OR operation requires boolean values at %A" t |> EvalException |> raise
    | Cond(t1, t2, t3) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResB true -> eval t2 env
        | ResB false -> eval t3 env
        | t1' -> sprintf "Term %A is not a Boolean value at %A" t1' t |> EvalException |> raise
    | Fn(pattern, t1) -> ResClosure(pattern, t1, env)
    | RecFn(id1, typ1, pattern, t) -> ResRecClosure(id1, pattern, t, env)
    | Let(pattern, t1, t2) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | t1' -> 
            match validatePattern pattern t1' env with
            | None -> ResRaise
            | Some env' -> eval t2 env'
    | Nil -> ResNil
    | IsEmpty(t1) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResNil -> ResB true
        | ResCons (_, _) -> ResB false
        | t1' -> sprintf "Term %A is not a list at %A" t1' t |> EvalException |> raise
    | Head(t1) -> 
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResCons (head, tail) -> head
        | ResNil -> ResRaise
        | t1' -> sprintf "Term %A is not a list at %A" t1' t |> EvalException |> raise
    | Tail(t1) -> 
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResCons (head, tail) -> tail
        | ResNil -> ResRaise
        | t1' -> sprintf "Term %A is not a list at %A" t1' t |> EvalException |> raise
    | Raise -> ResRaise
    | Try(t1, t2) ->
        match eval t1 env with
        | ResRaise -> eval t2 env
        | t1' -> t1'
    | Input ->
        Console.ReadLine().ToCharArray() |> Array.toList |> fromString
    | Output(t1) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResCons (ResC c, t) as t1' -> printf "%s" <| toString t1'; ResSkip
        | ResNil -> printfn ""; ResSkip
        | t1' -> sprintf "Term %A is not a string at %A" t1' t |> EvalException |> raise
    | Tuple(terms) ->
        if List.length terms < 2 then
            sprintf "Tuple must have more than 2 components at %A" t |> EvalException |> raise
    
        let f t =
            match eval t env with
            | ResRaise -> None
            | t' -> Some t'

        match mapOption f terms with
        | None -> ResRaise
        | Some results -> ResTuple results
    | Record(pairs) ->
        if Set(List.unzip pairs |> fst).Count < List.length pairs then
            sprintf "Record has duplicate fields at %A" t |> EvalException |> raise

        let f (name, t) =
            match eval t env with
            | ResRaise -> None
            | t' -> Some (name, t')

        match mapOption f pairs with
        | None -> ResRaise
        | Some results -> ResRecord results
    | ProjectIndex(n, t1) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResTuple values ->
            if n >= 0 && n < List.length values then
                List.nth values n
            else
                sprintf "Cannot acces index %A of tuple at %A" n t |> EvalException |> raise
        | t1' -> sprintf "Term %A is not a tuple at %A" t1' t |> EvalException |> raise
    | ProjectName(s, t1) ->
        match eval t1 env with
        | ResRaise -> ResRaise
        | ResRecord pairs ->
            let names, values = List.unzip pairs
            match Seq.tryFindIndex ((=) s) names with
            | Some i ->
                Seq.nth i values
            | None ->
                sprintf "Record has no entry %A at %A" s t |> EvalException |> raise
        | t1' -> sprintf "Term %A is not a record at %A" t1' t |> EvalException |> raise
    | X(id) -> 
        if env.ContainsKey id then
            env.[id]
        else
            sprintf "Could not find identifier %A" id |> EvalException |> raise


let evaluate t =
    eval t Map.empty