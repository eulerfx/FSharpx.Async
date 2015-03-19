namespace FSharpx.Control

/// A type which is not inhabited by any values.
type Void = Void 

/// An async pipeline which consumes values of type 'i, produces
/// values of type 'o and completes with a result 'a or an error.
type AsyncPipe<'i, 'o, 'a> = Async<AsyncPipeStep<'i, 'o, 'a>>

/// An individual step in an async pipeline.
and AsyncPipeStep<'i, 'o, 'a> =
  
  /// The pipeline completed with result 'a.
  | Done of 'a
  
  /// The pipeline is emitting a value of type 'o.
  | Emit of 'o * AsyncPipe<'i, 'o, 'a>
  
  /// The pipeline is consuming a value of type 'i.
  | Await of ('i option -> AsyncPipe<'i, 'o, 'a>)


/// An async pipeline which awaits inputs of type 'i and emits outputs of type 'o.
type AsyncPipe<'i, 'o> = AsyncPipe<'i, 'o, unit>

/// Consumes values of type 'i eventually returning value 'a.
type AsyncIn<'i, 'a> = AsyncPipe<'i, unit, 'a>

/// Consumes values of type 'i eventually stopping.
type AsyncIn<'i> = AsyncIn<'i, unit>

/// Emits values of type 'a eventually returning with value 'a.
type AsyncOut<'o, 'a> = AsyncPipe<Void, 'o, 'a>

/// Emits values of type 'a eventually stopping.
type AsyncOut<'o> = AsyncOut<'o, unit>

/// A source of effectful functions.
type AsyncChan<'i, 'o> = AsyncOut<'i -> Async<'o>>

/// A source of effectful sink functions.
///type AsyncSink<'o> = AsyncOut<'o -> Async<unit>>


/// Operations on async pipes.
module AsyncPipe =
    
  /// Creates an async pipe which halts immediately with the specified value.
  let doneWith (a:'a) : AsyncPipe<'i, 'o, 'a> =
    Done a |> async.Return

  /// Creates an async pipe which halts immediately.
  let inline doneWithUnit<'i, 'o> : AsyncPipe<'i, 'o> =
    doneWith ()

  /// Creates an async pipe which emits a value followed by the specified remainder.
  let emit (o:'o) (rest:AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o, 'a> = 
    Emit(o, rest) |> async.Return

  /// Creates an async pipe which emits a value and then stops immediately with the specified value.
  let emitDoneWith (o:'o) (a:'a) : AsyncPipe<'i, 'o, 'a> = 
    emit o (doneWith a)

  /// Creates an async pipe which emits a value and then stops immediately.
  let emitDoneWithUnit (o:'o) : AsyncPipe<'i, 'o> = 
    emitDoneWith o ()

  /// Creates an async pipe which awaits input and continues. An absence of input is indicated by None.
  let await (f:'i option -> AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o, 'a> =
    Await f |> async.Return

  /// Creates an async pipe which awaits input or returns the argument fallback pipe if the input is exhausted.
  let awaitOr (fb:AsyncPipe<'i, 'o, 'a>) (f:'i -> AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o, 'a> =
    await <| function
      | Some i -> f i
      | None -> fb

  /// Creates an async pipe which awaits a single value, returning None if input has been exhausted.
  let awaitOption<'a> : AsyncPipe<'a, 'a option> =
    awaitOr (emitDoneWithUnit None) <| fun i -> emitDoneWithUnit (Some i)

  /// Creates an async pipe which awaits input.
  let awaitOrDoneWith (a:'a) (f:'i -> AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o, 'a> =
    await <| function
      | Some i -> f i
      | None -> doneWith a

  /// Creates an async pipe which awaits input stopping if the input has been exhausted.
  let awaitOrDoneWithUnit (f:'i -> AsyncPipe<'i, 'o>) : AsyncPipe<'i, 'o> =
    awaitOrDoneWith () f

  /// Creates an async pipe which awaits a single input, emits and stops.
  let awaitOne<'a> : AsyncPipe<'a, 'a> =
    awaitOr doneWithUnit <| fun i -> emitDoneWithUnit i
        
  /// Maps over the input to an async pipe.        
  let rec mapIn (f:'j -> 'i) (p:AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'j, 'o, 'a> = async {
    let! p = p
    match p with
    | Done a -> return! doneWith a
    | Emit (o,rest) -> return! emit o (mapIn f rest)
    | Await g -> return! await (function 
      | Some j -> (f j) |> Some |> g |> mapIn f
      | None -> None |> g |> mapIn f) }

  /// Maps over the output of an async pipe.
  let rec mapOut (f:'o -> 'p) (p:AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'p, 'a> = async {
    let! p = p
    match p with
    | Done a -> return! doneWith a
    | Emit (o,rest) -> return! emit (f o) (mapOut f rest)
    | Await g -> return! await g |> mapOut f }

  /// Concetenates two async pipes.
  let rec append (p1:AsyncPipe<'i, 'o, unit>) (p2:AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o, 'a> = async {
    let! p1 = p1
    match p1 with
    | Done _ -> return! p2
    | Emit (h,t) -> return Emit (h, append t p2)
    | Await (recv) -> return Await ((fun a -> append (recv a) p2)) }   

  /// Binds two async pipes.
  let rec bind (f:'o -> AsyncPipe<'i, 'o2, unit>) (p:AsyncPipe<'i, 'o, 'a>) : AsyncPipe<'i, 'o2, 'a> = async {
    let! p = p
    match p with
    | Done a -> return Done a
    | Emit (h,t) -> return! append (f h) (bind f t)
    | Await (recv) -> return Await (recv >> bind f) }

  /// Repeats an async pipe indefinitely - when it reaches the end it starts the process over.
  let repeat (p:AsyncPipe<'i, 'o, _>) : AsyncPipe<'i, 'o, unit> =
    let rec loop p' = async {
      let! p' = p'
      match p' with
      | Done _ -> return! loop p
      | Emit (h,t) -> return Emit (h, loop t)
      | Await (recv) -> return Await (recv >> loop) }
    loop p
  
  /// Creates an async pipeline which buffers its inputs by count and emits buffers.
  let bufferByCount (n:int) : AsyncPipe<'a, 'a list> =
    let rec go acc n =
      match acc,n with
      | [],0 -> doneWith ()
      | acc,0 -> emitDoneWithUnit (acc |> List.rev)
      | acc,n -> await (function
        | Some a -> go (a::acc) (n - 1)
        | None -> go acc (n - 1)
      )
    go [] n

  /// Fuses two pipes together. 
  /// When the second pipe is done, the resulting pipe is done.
  /// When the second pipe emits, the resulting pipe emits. 
  /// When the second pipe awaits, the first pipe is invoked to see if it emits, in which case it emits into the second pipe.
  /// If the first pipe awaits, then the resulting pipe awaits.
  let rec compose (p1:AsyncPipe<'i, 'o, 'a>) (p2:AsyncPipe<'o, 'o2, 'a>) : AsyncPipe<'i, 'o2, 'a> = async {
    let! p2 = p2
    match p2 with
    | Done x -> 
      return Done x
    | Emit (h,t) -> 
      return Emit (h, compose p1 t)
    | Await recv2 ->
      let! p1 = p1
      match p1 with
      | Done x -> 
        return! compose (doneWith x) (recv2 None)
      | Emit (h,t) -> 
        return! compose t (recv2 (Some h))
      | Await (recv) -> 
        return Await ((fun a -> compose (recv a) (async.Return p2))) }

  /// Converts an async sequence into an async pipe which only emits values.
  let rec ofAsyncSeq (s:AsyncSeq<'a>) : AsyncOut<'a> =
    s |> Async.bind (function
      | Nil -> doneWith ()
      | Cons(x,xs) -> emit x (ofAsyncSeq xs) )

  /// Converts an async pipe which only emits values to an async sequence.
  let rec toAsyncSeq (p:AsyncOut<'a>) : AsyncSeq<'a> = 
    p |> Async.map (function
      | Await _ -> failwith "Should't happen! Void is uninhabited!"
      | Done _ -> Nil
      | Emit (a,tl) -> Cons(a, toAsyncSeq tl) )

  /// Creates a pipe which repeatedly awaits input, passes it to the provided function and emits its output.
  /// The pipe halts when no input is provided.
  let rec repeatFunc (f:'a -> Async<'b>) : AsyncPipe<'a, 'b> =
    awaitOrDoneWith () <| fun a -> 
      f a |> Async.bind (fun b -> emit b (repeatFunc f))

  /// Feeds an input value into a process.
  let rec feed (i:'i) (p:AsyncPipe<'i, _, _>) : AsyncPipe<'i, _, _> =
    p |> Async.bind (function
      | Done _ as p -> p |> async.Return
      | Emit (o,tl) -> Emit (o, feed i tl) |> async.Return
      | Await f -> Some i |> f)
    


  