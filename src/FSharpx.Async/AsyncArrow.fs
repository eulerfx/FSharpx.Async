namespace FSharpx.Control

open System
open System.Threading
open FSharpx.Control.Utils

/// <summary>
/// An async arrow is a function which produces an async computation as output.
/// </summary>
type AsyncArrow<'a, 'b> = 'a -> Async<'b>

/// An AsyncFilter is a mapping between arrows allowing for the input and output types to change.
type AsyncFilter<'a, 'b, 'c, 'd> = AsyncArrow<'a, 'b> -> AsyncArrow<'c, 'd>

/// A filter where the input and output types of the mapped arrows don't change.
type AsyncFilter<'a, 'b> = AsyncFilter<'a, 'b, 'a, 'b>


/// Operations on async arrows.
module AsyncArrow =

  /// Lifts a function into an arrow.  
  let inline lift (f:'a -> 'b) : AsyncArrow<'a, 'b> = 
    f >> async.Return

  /// The identity arrow.
  [<GeneralizableValue>]
  let identity<'a> : AsyncArrow<'a, 'a> = 
    lift id

  /// An arrow which applies an input arrow to an argument.
  [<GeneralizableValue>]
  let inline app<'a, 'b> : AsyncArrow<AsyncArrow<'a, 'b> * 'a, 'b> =
    fun (ar,a) -> ar a

  /// Creates an arrow which first invokes arrow f then g with the output of f.
  let inline compose (g:AsyncArrow<'b, 'c>) (f:AsyncArrow<'a, 'b>)  : AsyncArrow<'a, 'c> =
    fun a -> f a |> Async.bind g

  /// Creates an arrow which first invokes arrow f then g with the output of f.
  /// Alias for compose.
  let andThen = compose

  /// Maps over the input to an arrow.
  let mapl (f:'a2 -> 'a) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a2, 'b> =
    f >> a

  /// Maps over the input to an arrow.
  let maplAsync (f:AsyncArrow<'c, 'a>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'b> =
    f >> Async.bind a

  /// Maps over the output of an arrow.
  let mapr (f:'b -> 'c) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
    a >> Async.map f

  /// Maps over the output of an arrow.
  let maprAsync (f:AsyncArrow<'b, 'c>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'c> =
    a >> Async.bind f

  /// Maps over both the input and the output of an arrow.
  let dimap (f:'c -> 'a) (g:'b -> 'd) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'd> =
    f >> a >> Async.map g

  /// Maps over both the input and the output of an arrow.
  let dimapAsync (f:AsyncArrow<'c, 'a>) (g:AsyncArrow<'b, 'd>) (a:AsyncArrow<'a, 'b>) : AsyncArrow<'c, 'd> =
    f >> Async.bind a >> Async.bind g

  /// Creates an arrow which splits its inputs among the provided arrows.
  let inline split (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'a2, 'b2>) : AsyncArrow<'a * 'a2, 'b * 'b2> =
    fun (a,a2) -> Async.Parallel(f a, g a2)

  /// Creates an arrow which fans out the input to both arrows in parallel and collects the results.
  let inline fanout (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'a, 'c>) : AsyncArrow<'a, 'b * 'c> =
    fun a -> Async.Parallel(f a, g a)

  /// Creates an arrow which splits the input choice value between the two argument arrows.
  let inline fanin (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'c, 'b>) : AsyncArrow<Choice<'a, 'c>, 'b> =
    function
    | Choice1Of2 a -> f a 
    | Choice2Of2 c -> g c

  /// Creates an arrow which feeds Choice1Of2 inputs through the argument arrow, passing the rest through unchanged to the output.
  let inline left (f:AsyncArrow<'a, 'b>) : AsyncArrow<Choice<'a, 'c>, Choice<'b, 'c>> =
    function
    | Choice1Of2 a -> f a |> Async.map Choice1Of2
    | Choice2Of2 c -> Choice2Of2 c |> async.Return

  /// Creates an arrow which feeds Choice2Of2 inputs through the argument arrow, passing the rest through unchanged to the output.
  let inline right (f:AsyncArrow<'a, 'b>) : AsyncArrow<Choice<'c, 'a>, Choice<'c, 'b>> =
    function
    | Choice1Of2 c -> Choice1Of2 c |> async.Return
    | Choice2Of2 a -> f a |> Async.map Choice2Of2

  /// Creates an arrow which feeds the first argument to the argument arrow and passes the second one through.
  let inline first (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a * 'c, 'b * 'c> =
    fun (a,c) -> f a |> Async.map (fun b -> b,c)

  /// Creates an arrow which feeds the first argument to the argument arrow and passes the second one through.
  let inline second (f:AsyncArrow<'a, 'b>) : AsyncArrow<'c * 'a, 'c * 'b> =
    fun (c,a) -> f a |> Async.map (fun b -> c,b)

  /// Creates an arrow which propagates its input into the output.
  let inline inout (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'a * 'b> =
    fun a -> f a |> Async.map (fun b -> a,b)

  //// Invokes the argument arrow, but discards the result and passes its argument through the result.
  let inline inoutIgnore (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'a> =
    fun a -> f a |> Async.map (fun _ -> a)

  /// Creates an arrow which feeds a Some a to the argument arrow, otherwise returns None.
  let option (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a option, 'b option> =
    function
    | Some a -> f a |> Async.map Some
    | None -> async.Return None

  /// Creates an arrow which invokes the argument error on input Some a otherwise returns b.
  let optionOr (b:'b) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a option, 'b> =
    function
    | Some a -> f a
    | None -> async.Return b

  /// Creates an arrow which invokes the argument error on input Some a otherwise returns
  /// the result of b.
  let optionOrLazy (b:unit -> 'b) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a option, 'b> =
    function
    | Some a -> f a
    | None -> async.Return (b())  

  /// Creates an arrow which catches exceptions thrown by the async computation produces
  /// by the argument arrow.
  let inline catch (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, Choice<'b, exn>> =
    f >> Async.Catch

  /// Creates an arrow which throws an exception when the choice value
  /// produces by the argument arrow contains an error.
  let inline throw (f:AsyncArrow<'a, Choice<'b, exn>>) : AsyncArrow<'a, 'b> =
    fun a -> async {
      let! r = f a
      match r with
      | Choice1Of2 b -> return b
      | Choice2Of2 e -> return raise e
    }

  /// Creates an arrow which invokes the argument arrow and ignotes its output value.
  let inline ignore (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, unit> =
    f >> Async.Ignore

  /// Creates an arrow which filters inputs to an argument arrow based on the specified predicate.
  /// Returns the provided default value when the predicate isn't satisfied.
  let filterAsync (df:Async<'b>) (p:AsyncArrow<'a, bool>) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, 'b> =
    fun a -> p a |> Async.bind (function true -> f a | false -> df)  

  /// Creates an arrow which first invokes f then g with the input and output of f. The output of g is discarded.
  /// This is thenDoIn flipped.
  let doAfterIn (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'a * 'b, _>) : AsyncArrow<'a, 'b> =
    fun a -> f a |> Async.bind (fun b -> g (a,b) |> Async.map (fun _ -> b))

  /// Runs an arrow after the target arrow passing in the output of target arrow.
  /// This is thenDo flipped.
  let doAfter (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'b, _>)  : AsyncArrow<'a, 'b> =
    fun a -> f a |> Async.bind (fun b -> g b |> Async.map (fun _ -> b))
  
  /// Creates an arrow which first invokes f then g with the results of f. The output value of g is discarded.
  /// This is doAfterIn flipped.
  let inline thenDoIn (g:AsyncArrow<'a * 'b, _>) (f:AsyncArrow<'a, 'b>)  : AsyncArrow<'a, 'b> =
    doAfterIn f g    

  /// Creates an arrow which first invokes f then g with the results of f. The results (but not side-effects) of g are discarded.
  /// This is doAfter flipped.
  let inline thenDo (g:AsyncArrow<'b, _>) (f:AsyncArrow<'a, 'b>)  : AsyncArrow<'a, 'b> =
    doAfter f g

  /// Runs an arrow after the target arrow.    
  let doBefore (f:AsyncArrow<'a, 'b>) (g:AsyncArrow<'a, _>) : AsyncArrow<'a, 'b> =
    fun a -> g a |> Async.bind (fun _ -> f a)
   
  /// Creates an arrow which operates on arrays of inputs, applying in parallel but preserving input order.
  let arrayPar (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a[], 'b[]> =
    fun aa -> aa |> Array.map a |> Async.Parallel

  /// Lifts an arrow to operate on arrays of inputs, applying sequentially.
  let array (a:AsyncArrow<'a, 'b>) : AsyncArrow<'a[], 'b[]> =
    fun xs -> async {
      let ys = Array.zeroCreate (xs.Length)
      for i = 0 to xs.Length - 1 do
        let! y = a (xs.[i])
        ys.[i] <- y
      return ys        
    }

  /// Invokes the arrow g before arrow f. If g returns a left choice value which contains the input to f, 
  /// the resulting arrow will continue. Otherwise the right choice value is propagated.
  /// Reference implementation: g >>> (AsyncArrow.left f)
  let beforeChoice (g:AsyncArrow<'a, Choice<'a, 'c>>) (f:AsyncArrow<'a, 'b>) : AsyncArrow<'a, Choice<'b, 'c>> =
    compose (left f) g

  /// Invokes the arrow g with the successful result 'b of arrow f. If f returns a failure no action is performed.
  let afterSuccessAsync (g:AsyncArrow<'b, _>) (f:AsyncArrow<'a, Choice<'b, 'e>>) : AsyncArrow<'a, Choice<'b, 'e>> =
    f |> doAfter <| function
      | Choice1Of2 b -> g b
      | Choice2Of2 _ -> Async.unit

  /// Creates an arrow which first runs arrow g then if it returns a successful result runs f otherwise it
  /// propagates the error 'e.
  let composeChoice (g:AsyncArrow<'a, Choice<'b, 'e>>) (f:AsyncArrow<'b, Choice<'c, 'e>>) : AsyncArrow<'a, Choice<'c, 'e>> =
    g >> Async.bindChoice f

  /// Maps over the successful output of an arrow.
  let mapSuccess (g:'b -> 'c) (f:AsyncArrow<'a, Choice<'b, 'e>>) : AsyncArrow<'a, Choice<'c, 'e>> =
    f |> mapr (Choice.mapl g)

  /// Maps over the erroneous output of an arrow.
  /// Reference implementation: f |> AsyncArrow.mapr (Choice.mapr g)
  let mapError (g:'e -> 'f) (f:AsyncArrow<'a, Choice<'b, 'e>>) : AsyncArrow<'a, Choice<'b, 'f>> =
    f |> mapr (Choice.mapr g)

  /// Creates an arrow which first runs arrow g then if it returns a successful result runs f otherwise it
  /// propagates the error 'e1. If the arrow f fails, the error 'e2 is propagated.
  let andThenChoice_ (f:AsyncArrow<'b, Choice<'c, 'e2>>) (g:AsyncArrow<'a, Choice<'b, 'e1>>) : AsyncArrow<'a, Choice<'c, Choice<'e1, 'e2>>> =
    g >> Async.bindChoices f

  /// Creates an arrow which first runs arrow g then if it returns a successful result runs f otherwise it
  /// propagates the error 'e.
  /// Reference implementation: andThenTry_ f g |> mapError Choice.codiag     
  let andThenChoice (f:AsyncArrow<'b, Choice<'c, 'e>>) (g:AsyncArrow<'a, Choice<'b, 'e>>) : AsyncArrow<'a, Choice<'c, 'e>> =
    g >> Async.bindChoice f

  /// Creates an arrow which calls the specified callback function when an error 'e occurs.
  let notifyErrors (cb:'a * 'e -> unit) : AsyncFilter<'a, Choice<'b, 'e>> =
    thenDoIn <| fun (a,e) -> 
      match e with
      | Choice1Of2 _ -> Async.unit
      | Choice2Of2 e -> cb (a,e) ; Async.unit



/// Operations on async filters.
module AsyncFilter =
  
  /// The identity filter.
  [<GeneralizableValue>]
  let identity<'a, 'b> : AsyncFilter<'a, 'b, 'a, 'b> = 
    id

  /// Composes two async arrow filters such that f is followed by g.
  let inline compose (g:AsyncFilter<'c, 'd, 'e, 'f>) (f:AsyncFilter<'a, 'b, 'c, 'd>) : AsyncFilter<'a, 'b, 'e, 'f> =
    fun a -> f a |> g
  
  /// Composes two async arrow filters such that the second argument is followed by the first.
  /// This is an alias for compose.
  let andThen = compose



/// Async sink - a function which takes a value and produces an async computation.
type AsyncSink<'a> = AsyncArrow<'a, unit>

/// Operations on async sinks.
module AsyncSink =
    
  /// Maps a function over the input of a sink.
  let contramap (f:'b -> 'a) (s:AsyncSink<'a>) : AsyncSink<'b> =
    AsyncArrow.mapl f s

  /// Maps a function over the input of a sink.
  let contramapAsync (f:'b -> Async<'a>) (s:AsyncSink<'a>) : AsyncSink<'b> =
    AsyncArrow.maplAsync f s

  /// Filters inputs to a sink.
  let filterAsync (f:'a -> Async<bool>) (s:AsyncSink<'a>) : AsyncSink<'a> =
    s |> AsyncArrow.filterAsync Async.unit f

  /// Filters inputs to a sink.
  let filter (f:'a -> bool) (s:AsyncSink<'a>) : AsyncSink<'a> =
    filterAsync (f >> async.Return) s

  /// Maps and filters inputs to a sink.
  let contrachooseAsync (f:'b -> Async<'a option>) (s:AsyncSink<'a>) : AsyncSink<'b> =
    f >> Async.bind (function
      | Some a -> s a
      | None -> Async.unit
    )          
  
  /// Maps and filters inputs to a sink.
  let contrachoose (f:'b -> 'a option) (s:AsyncSink<'a>) : AsyncSink<'b> =
    contrachooseAsync (f >> async.Return) s

  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  let merge (s1:AsyncSink<'a>) (s2:AsyncSink<'a>) : AsyncSink<'a> =
    fun a -> s1 a |> Async.bind (fun _ -> s2 a)

  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  /// This merge with arguments flipped.
  let inline andThen s2 s1 = merge s1 s2

  /// Creates a sink which invokes the underlying sinks sequentially.
  let mergeAll (xs:seq<AsyncSink<'a>>) : AsyncSink<'a> =
    fun a -> async {
      for s in xs do
        do! s a }

  /// Creates a sink which invokes the underlying sinks in parallel.
  let mergeAllPar (xs:seq<AsyncSink<'a>>) : AsyncSink<'a> =
    fun a -> xs |> Seq.map ((|>) a) |> Async.Parallel |> Async.Ignore

  /// Merges two sinks such that the resulting sink invokes the first one, then the second one.
  let mergePar (s1:AsyncSink<'a>) (s2:AsyncSink<'a>) : AsyncSink<'a> =
    mergeAllPar [ s1 ; s2 ]

  /// Lifts an async sink to operate on sequences of the input type in parallel.
  let seqPar (s:AsyncSink<'a>) : AsyncSink<seq<'a>> =
    Seq.map s >> Async.Parallel >> Async.Ignore

  /// Lifts an async sink to operate on sequences of the input type sequetially.
  let seq (s:AsyncSink<'a>) : AsyncSink<seq<'a>> =
    fun xs -> async { for x in xs do do! s x }
  