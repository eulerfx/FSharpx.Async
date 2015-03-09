namespace FSharpx.Control

type private AgentChMsg<'a> =
  | Fill of 'a * AsyncReplyChannel<unit>
  | Take of AsyncReplyChannel<'a>

/// A synchronous channel with async-based notifications.
type Chan<'a>() =
  
  let agent : MailboxProcessor<AgentChMsg<'a>> = 
    MailboxProcessor.Start <| fun agent ->
      let rec loop() = async {
        let! msg = agent.Receive()
        match msg with
        | Fill (a,ch) ->           
          let! ch' = agent.Scan(function
            | Take ch' -> Some (ch' |> async.Return)
            | _ -> None)
          ch'.Reply(a)
          ch.Reply()
          return! loop()
        | Take ch -> 
          let! a,ch' = agent.Scan(function
            | Fill (a,ch) -> Some ((a,ch) |> async.Return)
            | _ -> None)                      
          ch.Reply(a)
          if (not <| obj.ReferenceEquals(ch',null)) then
            ch'.Reply()
          return! loop()
      }      
      loop()
 
  /// Creates an async computation which completes when a value is available in the channel.
  member this.Take() =
    agent.PostAndAsyncReply(fun ch -> Take(ch))
  
  /// Creates an async computation which completes when the provided value is read from the channel. 
  member this.Fill(a) =
    agent.PostAndAsyncReply(fun ch -> Fill(a,ch))

  /// Queues a write to the channel.
  member this.EnqueueFill(a) =
    agent.Post(Fill(a,Unchecked.defaultof<_>))  

  with 
    interface System.IDisposable with
      member x.Dispose() = 
        (agent :> System.IDisposable).Dispose()


/// Operations on channels.
module Chan =
  
  /// Creates an empty channel.
  let inline create() = new Chan<'a>()

  /// Creates a channel initialized with a value.
  let inline createFull a = 
    let ch = create()
    ch.EnqueueFill a
    ch

  /// Creates an async computation which completes when a value is available in the channel.
  let inline take (ch:Chan<'a>) = ch.Take()

  /// Blocks until a value is available in the channel.
  let inline takeNow (ch:Chan<'a>) = take ch |> Async.RunSynchronously

  /// Creates an async computation which completes when the provided value is read from the channel. 
  let inline fill a (ch:Chan<'a>) = ch.Fill a

  /// Starts an async computation which writes a value to a channel.
  let inline fillNow a (ch:Chan<'a>) = fill a ch |> Async.Start

  /// Creates an async computation which completes when the provided value is read from the channel. 
  let inline enqueueFill a (ch:Chan<'a>) = ch.EnqueueFill a




/// A channel which buffers inputs by size and time.
type BufferCh<'a> = internal {
  inCh : Chan<'a>
  outCh : Chan<'a[]>
}

/// Operations on buffer channels.
module BufferCh =
  
  open System

  /// Creates a buffer channel which buffers inputs by the specified size and time.
  let create (bufferSize:int) (bufferTimeMs:int) : BufferCh<'a> =
    let c = { inCh = Chan.create() ; outCh = Chan.create() }
    let buf = ResizeArray<'a>(bufferSize)
    let rec loop (ttl:int) = async {
      let t0 = DateTime.UtcNow.Millisecond
      let! a = Chan.take c.inCh |> Async.timeoutAfter (TimeSpan.FromMilliseconds (float ttl))
      buf.Add(a)
      if ((ttl <= 0 && buf.Count > 0) || buf.Count = bufferSize) then
        do! c.outCh |> Chan.fill (buf.ToArray())
        buf.Clear()
        return! loop(bufferTimeMs)
      else
        let t1 = DateTime.UtcNow.Millisecond
        return! loop(ttl - (t1 - t0))
    }
    Async.Start <| loop bufferTimeMs
    c
    
  /// Puts an item into a buffer channel.
  let put (a:'a) (ch:BufferCh<'a>) : Async<unit> =
    ch.inCh |> Chan.fill a

  /// Creates an async computation which produces a batch produced by the buffer channel
  /// when it is available.
  let get (ch:BufferCh<'a>) : Async<'a[]> =
    ch.outCh |> Chan.take