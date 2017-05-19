// Learn more about F# at http://fsharp.org

//#r "Microsoft.Build.dll"
#r @"../../external/nuget/microsoft.build/15.1.548/lib/net46/Microsoft.Build.dll"

open System
open System.IO

open Microsoft.Build

let handleProject (prj:Construction.ProjectInSolution) =
    printfn "%s" prj.AbsolutePath
    prj.Dependencies |> Seq.iter (printfn " - %s")
    ()

let extractXmlTextTo (writer:TextWriter) prjFile =
    let readLines (fname:string) = seq {
        use sr = new StreamReader (fname)
        while not sr.EndOfStream do
            yield sr.ReadLine ()
    }
    prjFile
        |> readLines
        |> Seq.filter (fun (line:string) -> not (line.Contains "<?xml version"))
        |> Seq.iter writer.WriteLine

let findProjectsInSolution solutionFile :string seq = 
    let fname = Path.GetFullPath solutionFile
    let sln = Construction.SolutionFile.Parse fname
    sln.ProjectsInOrder |> Seq.map (fun prj -> prj.AbsolutePath)

let usage = """
Usage:
    <solution-path> <output-path {default=Sample.csprojs.xml}>
"""    

type Error = string option

//[<EntryPoint>]
let main argv =

    //printfn "called %A" argv

    let cmd,args = argv |> Array.toList |> function 
            | [] -> "AnalyzeSln",[]
            | cmd::args -> cmd,args

    let handle sln writer =
        sln
            |> findProjectsInSolution
            |> Seq.filter (fun prj -> prj.EndsWith ".csproj")
            |> Seq.iter (extractXmlTextTo writer)
        None

    let defSln = "../All.sln"
    let defXml = System.Console.Out

    let (error:Error) = 
        match args with
        | [sln] -> handle sln defXml
        | [sln;xml] -> handle sln (new StreamWriter(xml))
        | [] -> Some usage
        | other -> Some (sprintf "Invalid args: %A" other)

    error |> function
        | None ->
            0
        | Some text ->
            eprintfn "%s" text
            1

main fsi.CommandLineArgs

