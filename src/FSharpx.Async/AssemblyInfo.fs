﻿namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("FSharpx.Async")>]
[<assembly: AssemblyProductAttribute("FSharpx.Async")>]
[<assembly: AssemblyDescriptionAttribute("Async extensions for F#")>]
[<assembly: AssemblyVersionAttribute("1.11.1")>]
[<assembly: AssemblyFileVersionAttribute("1.11.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.11.1"
