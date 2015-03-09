namespace FSharpx.Control

open System
open System.Threading

/// An async observer is a function which receives notifications from an event source.
/// A notification can either be a value of type 'a or a completion
/// notification with a possible error.
type AsyncObs<'a> = AsyncSink<Choice<'a, exn option>>

/// An async event represented as a function which takes an async observer
/// and returns an async computation which runs for the duration of the
/// subscription.
type AsyncEvent<'a> = AsyncSink<AsyncObs<'a>>

/// Operations on async observers.
module AsyncObs =

  /// Creates an observer from an async sink which takes values of type 'a
  /// as input. In case of error or completion an empty async computation is
  /// returned.
  let ofSink (s:AsyncSink<'a>) : AsyncObs<'a> =
    function
    | Choice1Of2 a -> s a
    | _ -> Async.unit

  /// Creates an async observer given explicit callbacks for an item notification,
  /// an error notification and a successful completion notification.
  let ofCont (item:AsyncSink<'a>) (error:AsyncSink<exn>) (ok:AsyncSink<unit>) : AsyncObs<'a> =
    function
    | Choice1Of2 a -> item a
    | Choice2Of2 (Some e) -> error e
    | Choice2Of2 None -> ok ()

  /// Notifies an observer about an item.
  let inline item (a:'a) (o:AsyncObs<'a>) =
    a |> Choice1Of2 |> o

  /// Notifies an observer about completion.
  let inline complete e (o:AsyncObs<'a>) =
    e |> Choice2Of2 |> o

  /// Notifies an observer about successful completion.
  let inline completeOk (o:AsyncObs<'a>) =
    complete None o

  /// Notifies an observer about an erroneous completion.
  let inline error (e:exn) (o:AsyncObs<'a>) =
    complete (Some e) o

  /// Maps over the input of an observer.
  let contramapAsync (f:'b -> Async<'a>) (o:AsyncObs<'a>) : AsyncObs<'b> =
    function
    | Choice1Of2 b -> f b |> Async.bind (fun b -> item b o)
    | Choice2Of2 e -> complete e o

  /// Filters inputs to an observer based on the specified predicate.
  let filterAsync (p:'a -> Async<bool>) (o:AsyncObs<'a>) : AsyncObs<'a> =
    function
    | Choice1Of2 a -> p a |> Async.bind (function true -> item a o | false -> Async.unit)
    | Choice2Of2 _ as x -> o x

  /// Filters and maps inputs to an observer based on the specified chooser function.
  let contrachooseAsync (f:'b -> Async<'a option>) (o:AsyncObs<'a>) : AsyncObs<'b> =
    function
    | Choice1Of2 b -> f b |> Async.bind (function Some a -> item a o | None -> Async.unit)
    | Choice2Of2 e -> complete e o


/// Operations on async events.
module AsyncEvent =
  
  /// An empty event which immediately signals completion.
  [<GeneralizableValue>]
  let empty<'a> : AsyncEvent<'a> =
    AsyncObs.completeOk

  /// Subscribes an observer to an event.
  let inline add (e:AsyncEvent<'a>) (o:AsyncObs<'a>) =
    e o
  
  /// Creates an async event which publishes a single item
  /// followed by a successful completion notification.
  let singleton (a:'a) : AsyncEvent<'a> =
    fun obs ->
      AsyncObs.item a obs 
      |> Async.bind (fun _ -> AsyncObs.completeOk obs)

  /// Creates an async event from a thunk.
  let inline delay (f:unit -> AsyncEvent<'a>) : AsyncEvent<'a> = 
    fun obs -> f() obs

  /// Maps over items of an async event.
  let inline mapAsync (f:'a -> Async<'b>) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    e << AsyncObs.contramapAsync f

  /// Maps over items of an async event.
  let inline map (f:'a -> 'b) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    mapAsync (f >> async.Return) e

  /// Filters items emitted by an async event using the specified predicate.
  let inline filterAsync (p:'a -> Async<bool>) (e:AsyncEvent<'a>) : AsyncEvent<'a> =
    e << AsyncObs.filterAsync p

  /// Filters items emitted by an async event using the specified predicate.
  let filter (p:'a -> bool) (e:AsyncEvent<'a>) : AsyncEvent<'a> =
    filterAsync (p >> async.Return) e

  /// Maps and filters items emitted by an async event using the specified chooser function.
  let inline chooseAsync (f:'a -> Async<'b option>) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    e << AsyncObs.contrachooseAsync f

  /// Maps and filters items emitted by an async event using the specified chooser function.
  let choose (f:'a -> 'b option) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    chooseAsync (f >> async.Return) e

  /// Flattens an async event of async events by subscribing observers
  /// to each inner event emitted by the outer event.
  let join (e:AsyncEvent<AsyncEvent<'a>>) : AsyncEvent<'a> =
    fun (obs:AsyncObs<'a>) ->
      add e <| 
        function
        | Choice1Of2 e -> add e obs
        | Choice2Of2 e -> AsyncObs.complete e obs

  /// Binds each item produced by an event to another event and flattens
  /// the resulting event.
  let bind (k:'a -> AsyncEvent<'b>) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    e |> map k |> join

  /// Creates an async computation which completes when either the last item
  /// has been received or the event completes without emitting any items.
  let last (o:AsyncEvent<'a>) : Async<'a option> = async {
    let last : ref<option<'a>> = ref None
    use mre = new ManualResetEventSlim()
    do! add o <| function 
      | Choice1Of2 a -> last := Some a ; Async.unit
      | Choice2Of2 _ -> mre.Set() ; Async.unit
    do! mre.WaitHandle |> Async.AwaitWaitHandle |> Async.Ignore                    
    return !last }     

  /// Scans over items produces by an async event using the specified function
  /// emitting results as they are produces.
  let scanAsync (f:'b -> 'a -> Async<'b>) (b:'b) (e:AsyncEvent<'a>) : AsyncEvent<'b> =
    fun (obs:AsyncObs<'b>) ->
      let b = ref b 
      add e <| 
        function
        | Choice2Of2 e -> AsyncObs.complete e obs
        | Choice1Of2 a -> async {
            let! b' = f (!b) a
            b := b'
            do! AsyncObs.item b' obs }

  /// Folds over an async event using the specified fold function and default value.
  let foldAsync (f:'b -> 'a -> Async<'b>) (b:'b) (o:AsyncEvent<'a>) : Async<'b> =
    scanAsync f b o |> last |> Async.map (Option.fold (fun _ a -> a) b)

  /// Merges two async events into one such that observers of the resulting event
  /// are subscribed to both argument events in parallel.
  let merge (e1:AsyncEvent<'a>) (e2:AsyncEvent<'a>) : AsyncEvent<'a> = 
    fun (obs:AsyncObs<'a>) ->
      Async.Parallel [ e1 obs ; e2 obs] |> Async.Ignore

  /// Generates an async event based on the specified generator function and initial state.
  let unfoldAsync (f:'State -> Async<('a * 'State) option>) (s:'State) : AsyncEvent<'a> =        
    fun (obs:AsyncObs<'a>) ->
      let rec go s = async {
        match s with
        | Some (a,s) ->             
          let! s',_ = Async.Parallel(f s, AsyncObs.item a obs)                        
          return! go s'
        | None ->
          do! AsyncObs.completeOk obs
          return ()
      }         
      f s |> Async.bind go
