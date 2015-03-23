﻿module AsyncSeqTests

open NUnit.Framework
open FSharpx.Control

/// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
let EQ (a:AsyncSeq<'a>) (b:AsyncSeq<'a>) = 
  let exp = a |> AsyncSeq.toList |> Async.RunSynchronously
  let act = b |> AsyncSeq.toList |> Async.RunSynchronously  
  if (exp = act) then true
  else
    printfn "expected=%A" exp
    printfn "actual=%A" act
    false


[<Test>]
let ``AsyncSeq.toArray``() =  
  let ls = [1;2;3]
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toArray |> Async.RunSynchronously |> Array.toList
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.toList``() =  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.concatSeq``() =  
  let ls = [ [1;2] ; [3;4] ]
  let actual = AsyncSeq.ofSeq ls |> AsyncSeq.concatSeq    
  let expected = ls |> List.concat |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.unfoldAsync``() =  
  let gen s =
    if s < 3 then (s,s + 1) |> Some
    else None
  let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq
  let actual = AsyncSeq.unfoldAsync (gen >> async.Return) 0
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.interleave``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3] = merged)


[<Test>]
let ``AsyncSeq.bufferByCount``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    yield 4
    yield 5
  }
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toList |> Async.RunSynchronously
  Assert.True(([[|1;2|];[|3;4|];[|5|]] = s'))


[<Test>]
let ``AsyncSeq.zip``() =  
  let la = [1;2;3;4;5]
  let lb = [1;2;3;4;5]
  let a = la |> AsyncSeq.ofSeq
  let b = lb |> AsyncSeq.ofSeq
  let actual = AsyncSeq.zip a b
  let expected = List.zip la lb |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.zipWithAsync``() =  
  let la = [1;2;3;4;5]
  let lb = [1;2;3;4;5]
  let a = la |> AsyncSeq.ofSeq
  let b = lb |> AsyncSeq.ofSeq
  let actual = AsyncSeq.zipWithAsync (fun a b -> a + b |> async.Return) a b
  let expected = List.zip la lb |> List.map ((<||) (+)) |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.iterWhileAsync``() =  
  let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
  let seen = ResizeArray<_>()
  s |> AsyncSeq.iterWhileAsync (fun x -> seen.Add(x) ; (x < 3) |> async.Return) |> Async.RunSynchronously
  Assert.True((seen |> List.ofSeq = [1;2;3]))


[<Test>]
let ``AsyncSeq.skipWhileAsync``() =  
  let ls = [1;2;3;4;5]
  let p i = i <= 2
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skipWhileAsync (p >> async.Return)
  let expected = ls |> Seq.skipWhile p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.takeWhileAsync``() =  
  let ls = [1;2;3;4;5]
  let p i = i < 4
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileAsync (p >> async.Return)
  let expected = ls |> Seq.takeWhile p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.take``() =  
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c
  let expected = ls |> Seq.take c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skip``() =
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.threadStateAsync``() =
  let ls = [1;2;3;4;5]
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.threadStateAsync (fun i a -> async.Return(i + a, i + 1)) 0
  let expected = [1;3;5;7;9] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.scanAsync``() =
  let ls = [1;2;3;4;5]
  let f i a = i + a
  let z = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scanAsync (fun i a -> f i a |> async.Return) z
  let expected = ls |> Seq.scan f z |> Seq.skip 1 |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.foldAsync``() =
  let ls = [1;2;3;4;5]
  let f i a = i + a
  let z = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.foldAsync (fun i a -> f i a |> async.Return) z |> Async.RunSynchronously
  let expected = ls |> Seq.fold f z
  Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.filterAsync``() =
  let ls = [1;2;3;4;5]
  let p i = i > 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filterAsync (p >> async.Return)
  let expected = ls |> Seq.filter p |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.merge``() =
  let ls1 = [1;2;3;4;5]
  let ls2 = [6;7;8;9;10]
  let actual = AsyncSeq.merge (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toList |> Async.RunSynchronously |> Set.ofList
  let expected = ls1 @ ls2 |> Set.ofList
  Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.merge should be fair``() =  
  let s1 = asyncSeq {
    do! Async.Sleep 10
    yield 1
  }
  let s2 = asyncSeq {
    yield 2
  }
  let actual = AsyncSeq.merge s1 s2
  let expected = [2;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

//[<Test>]
let ``AsyncSeq.groupBy``() =
  
  let s = asyncSeq {
    yield "a1"
    yield "a2"
    yield "b1"
    yield "b2"
  }

  let s' = 
    s
    |> AsyncSeq.groupBy 
        (fun x -> x.[0]) 
        (fun _ _ ss -> 
          ss |> async.Return)

  let s'' =
    s'
    |> AsyncSeq.mapAsync (fun ss -> 
      printfn "folding substream..." 
      AsyncSeq.toList ss
    )
    //|> AsyncSeq.toBlockingSeq
    //|> List.ofSeq
    |> AsyncSeq.toList
    |> Async.RunSynchronously

  let expect = [ ["a1";"a2"] ; ["b1";"b2"] ]

  Assert.True((expect = s''))
