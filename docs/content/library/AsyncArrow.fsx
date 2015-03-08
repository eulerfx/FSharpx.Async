(**

# F# Async: AsyncArrow

An `AsyncArrow<'a, 'b>` is a function which takes a value of type `'a` as input and returns
and async computation which produces a value of type `'b`. An async arrow is represented
as a type alias `type AsyncArrow<'a, 'b> = 'a -> Async<'b>`. An async arrow can represent an 
async request-reply protocol and therefore operations on arrows are operations on said protocols.
Many of the operations defined on ordinary functions, such as composition `>>` can also be 
defined for async arrows by simply lifting them into the `Async` type with operators defined therein.

Async arrows are useful beyond the `Async` type alone when there is a need to consider the input
which creates the async computation. Arrows also capture common ways of *gluing* together async request-reply
protocols. Furthermore, mappings between async arrows, called async filters, can also be useful to consider as a 
reified type for implementing service pipelines.

The `AsyncArrow` type is located in the `FSharpx.Async.dll` assembly which can be loaded in F# Interactive as follows:
*)

#r "../../../bin/FSharpx.Async.dll"
#r "System.Net.Http.dll"
open FSharpx.Control


(**
## Motivation

For example, consider an HTTP request-reply protocol as provided
by [System.Net.Http.HttpClient](https://msdn.microsoft.com/en-us/library/system.net.http.httpclient%28v=vs.118%29.aspx). 
The method [HttpClient.SendAsync](https://msdn.microsoft.com/en-us/library/hh138176(v=vs.118).aspx) 
when adapted to `Async` has type `HttpRequestMessage -> Async<HttpResponseMessage>`. A specific instance
of `HttpClient` can be constructed and then passed around as a function value. For brevity we also alias
the HTTP message types:
*)

open System.Net
open System.Net.Http

type HttpReq = HttpRequestMessage

type HttpRes = HttpResponseMessage

let httpClient = new HttpClient()

let httpService : HttpReq -> Async<HttpRes> = 
  httpClient.SendAsync >> Async.AwaitTask


(**
Suppose you wanted to modify this function such that a header is added to the HTTP response before it is returned. This can be
done as follows:
*)

let httpServiceWithHeader : HttpReq -> Async<HttpRes> =
  httpService >> Async.map (fun res ->
    res.Content.Headers.Add("X-Hello", "World")
    res)

(**
Similarly to modify the headers of the incoming HTTP request:
*)

let httpServiceWithHeader' : HttpReq -> Async<HttpRes> =
  fun req -> 
    req.Headers.Add("X-Foo", "Bar")
    httpServiceWithHeader req

(**
Now the value `httpServiceWithHeader'` is an HTTP service which calls `HttpClient` but adds a header to both
the incoming HTTP request and to the outgoing HTTP response. Up to this point we've relied on existing mechanisms
provided by `Async` itself. Suppose next that we wanted to measure the latency of HTTP interactions. This can be
done as follows:
*)

let httpServiceTimed : HttpReq -> Async<HttpRes> =
  fun req -> async {
    let sw = System.Diagnostics.Stopwatch.StartNew()
    let! res = httpServiceWithHeader' req
    sw.Stop()
    printfn "elapsed_ms=%i" sw.ElapsedMilliseconds
    return res
  }

(**
This timing logic can be useful outside of the specific HTTP service that we've composed. Let us abstract it:
*)

let httpTimer (service:HttpReq -> Async<HttpRes>) (req:HttpReq) =
  async {
    let sw = System.Diagnostics.Stopwatch.StartNew()
    let! res = service req
    sw.Stop()
    printfn "elapsed_ms=%i" sw.ElapsedMilliseconds
    return res    
  }

(**
The value `httpTimer` is a function which takes an HTTP service and produces another HTTP service which wraps the
argument service with a timer. If we view the HTTP service as an async arrow `AsyncArrow<HttpReq, HttpRes>`
then the above value is a mapping between async arrows - an async filter of type `AsyncFilter<HttpReq, HttpRes>`.
The previous operations which modify HTTP request and response headers can also be abstracted into async filters.

What do we gain by reifying the async arrow and async filter types? Composition of course! We can declare the types and 
then see what operations can be defined on them. Once we abstract the above logic into filters we can glue them 
back together in various ways. Here is one:
*)

let reqHeaderFilter : AsyncFilter<HttpReq, HttpRes> =
  fun service req -> async {
    req.Headers.Add("X-Foo", "Bar") 
    return! service req 
  }

let resHeaderFilter : AsyncFilter<HttpReq, HttpRes> =
  fun service req -> async {
    let! res = service req 
    res.Content.Headers.Add("X-Hello", "World")
    return res
  }

let compositeFilter : AsyncFilter<HttpReq, HttpRes> =
  reqHeaderFilter  
  |> AsyncFilter.andThen resHeaderFilter
  |> AsyncFilter.andThen httpTimer

(**
We've created a filter pipeline using the operator `AsyncFilter.andThen` which composes two filters into one. Since an async
filter is just a function between async arrows, we can apply it to the original HTTP service as follows:
*)

let filteredService : AsyncArrow<HttpReq, HttpRes> =
  httpService |> compositeFilter

(**
Note that the order of composition matters - it determines the order in which the filters in the pipeline are invoked.

The filters defined above leave the type of the async arrows unchanged. In general, since a filter is a mapping between
arrows, we can define filters which change the input and output types of the corresponding arrows. Suppose that we are 
implementing an HTTP service which exposes an underlying domain model. We would like to use the rich F# type system to
define our domain and then adapt it to the HTTP protocol. Filters allow us to separate concerns - we can define a filter
which takes an arrow operating on domain specific input and output types and map it to an arrow based on HTTP types as seen 
above. To do this we first define encoding functions:
*)

/// A domain-specific input type.
type Input = {
  id : string
}

/// A domain-specific output type.
type Output = {
  text : string
}

/// Decodes an HTTP request into a domain-specific input.
let decode (req:HttpReq) = async {
  let! id = req.Content.ReadAsStringAsync() |> Async.AwaitTask
  return {
    Input.id = id
  }
}

/// Encodes a domain-specific output into an HTTP response.
let encode (o:Output) = async {
  let res = new HttpRes()
  res.Content <- new StringContent(o.text)
  return res
}

(**
Next we abstract the encoding mechanism into a filter. In this case however, the filter will change the input and output types
of the corresponding arrows:
*)

let codecFilter 
  (dec:HttpRequestMessage -> Async<'i>, 
   enc:'o -> Async<HttpRes>) : AsyncFilter<'i, 'o, HttpReq, HttpRes> =
  AsyncArrow.maplAsync dec 
  |> AsyncFilter.andThen (AsyncArrow.maprAsync enc)

(**
The value `codecFilter` is a function which when given decoder and encoder function creates an async filter which takes an
async arrow based on the encoded types `'i` and `'o` and maps it to an async arrow based on HTTP. This filter can be used as
follows:
*)

let myService (i:Input) = async {
  return {
    Output.text = i.id
  }
}

let myHttpService : AsyncArrow<HttpReq, HttpRes> = 
  myService |> codecFilter (decode,encode)

(**
Arrows and filters allowed us to separate the concerns of HTTP from the concerns of the domain-specific service as well as cross-cutting
concerns such as timing. The `codecFilter` defined above can be made more generic or defined for a specific format such as JSON. This 
example merely scratches the surface of what can be done with filters and there is a myriad of other filters we can define - authorization, logging, 
routing, etc.
*)


(**

## Async sinks

A async sink is a specific type of arrow which produces `unit` as output `type AsyncSink<'a> = AsyncArrow<'a, unit>`. This particular 
type of arrow is interesting because it captures the notion of a side-effect. Since we've specialized the output type of an arrow to `unit` 
we can define operations on async sinks that can't be defined on arrows in general.

The domain-specific service `myService` defined above simply echoes its input. A real world service will usually do much more. A 
common task in services is to store the results of the operation in a database. We can model a service which stores output in
a database with type `Output -> Async<unit>`. In addition to storing the output of a service to a database we may wish to publish
a message on a queue to notify subscribing parties. A service to publish a message on a queue can also be modeled type with type
`Output -> Async<unit>`. In both cases we have an async sink `AsyncSink<Output>`. Representing these capabilities as sinks allows
us to compose them as follows:
*)


let saveToDb : AsyncSink<Output> =
  fun o -> Async.unit

let publishToQueue : AsyncSink<Output> =
  fun o -> Async.unit

let saveThenPublish : AsyncSink<Output> =
  saveToDb 
  |> AsyncSink.andThen publishToQueue


(**
The resulting sink `saveThenPublish` first invokes service `saveToDb` then `publishToQueue`. Order may be of the essence - if the
database operation fails, publishing an message on a queue can cause inconsistencies. If the operations are completely independent 
then we can compose them in parallel using `AsyncSink.mergePar`. We can apply this sink to the service defined above as follows:
*)

let myServiceWithSink : AsyncArrow<Input, Output> =
  myService
  |> AsyncArrow.thenDo saveThenPublish


(**

## Error handling

F# encourages the representation of errors explictly in the type system rather than using an out of band mechanism such as exceptions.
An operation which produces a value of type `'a` but which may fail with error type `'e` can be represented using the choice type as 
`Choice<'a, 'e>`. The operator `Async.Catch` catches any exception thrown by an async computation and reifies it as a value of type
`Choice<'a, exn>`. Async arrows can capture common patterns of handling errors and help us compose services which may error.

For example, suppose the domain-specific service `myService` define above is extended to support explicit errors. Changing its output type
to `Choice<Output, exn>` will force us to handle the error explictly. Arrows and filters once again allows us to separate concerns
into well defined modules:
*)

/// A domain-specific service which may error.
let myServiceErr (i:Input) = async {
  if (i.id = "foo") then 
    return Choice2Of2 (exn("oh no!"))
  else
    return {
      Output.text = i.id
    }
    |> Choice1Of2
}

/// An explicit encoder for errors.
let encodeErr (ex:exn) = async {
  let res = new HttpRes(HttpStatusCode.BadRequest)
  res.Content <- new StringContent(ex.Message)
  return res
}


module Choice =
  
  /// Folds a choice value by handling both cases explicitly.  
  let fold (f:'a -> 'b) (g:'e -> 'b) = function
    | Choice1Of2 a -> f a
    | Choice2Of2 e -> g e


/// A service which invokes the sink defined above upon success.  
let myServiceErrSink =
  myServiceErr 
  |> AsyncArrow.afterSuccessAsync saveThenPublish
  

/// An HTTP service which handles errors explicitly.
let myHttpServiceErr : AsyncArrow<HttpReq, HttpRes> = 
  myServiceErrSink |> codecFilter (decode, Choice.fold encode encodeErr)


(**

We are able to introduce explicit errors into our services while still retaining compositionality and separation of concerns. To this end
we've defined an explicit encoder for errors which simply returns an HTTP 404 response. Next we composed the service such that errors
are threaded through but the operation to write to the database and publish on a queue is only invoked when the output is successful.

*)


(**

## Caution

It can be easy to get carried away with async arrows and filters. Many of the operations on async arrows simply compose `Async` in 
specific ways. As such, it is possible to duplicate a lot of functionality already provided by `Async` itself. It can be tempting
to define entire libraries in terms of arrows so as to provide a uniform programming model. Its important to remember that arrows
and filters should serve as the *glue* for composing services, not services in their own right. Before defining a new arrow or filter
consider whether it can be implemented with existing operators.

*)

(**

## Relationship to Async

Arrows as defined herein are tightly coupled to the `Async` type. The relationship is deeper still however. The operation `Async.bind`
defined on `Async` (and likewise for all *monads* ) has type `('a -> Async<'b>) -> (Async<'a> -> Async<'b>)`. Notably it takes an
async arrow as the first argument and produces a mapping between `Async<'a>` and `Async<'b>`. This differs from arrow composition
in that the value `Async<'a>` is provided explicitly. If we parametarize `Async<'a>` by a value of type `'x` then we have composition
of arrows `('a -> Async<'b>) -> ('x - Async<'a>) -> ('x -> Async<'b>)` as captured by `AsyncArrow.compose`. Indeed, this is a slightly
different take on monads - rather than defining a monad with `return` and `bind` operations we can think of monads as enabling composition
of arrows much like function composition. To be more precise, arrows of the form `'a -> M<'b>` for some monadic type `M` are known as *Kleisli arrows*.
*)


(**

# Further Reading

* [Your Server as a Function](http://monkey.org/~marius/funsrv.pdf)
* [Generalising Monads to Arrows](http://www.cse.chalmers.se/~rjmh/Papers/arrows.pdf)
* [Arrow](http://en.wikipedia.org/wiki/Arrow_%28computer_science%29)
* [Monads, Kleisli Arrows, Comonads and other Rambling Thoughts](http://blog.sigfpe.com/2006/06/monads-kleisli-arrows-comonads-and.html)

*)