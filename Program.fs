// Convert .csproj to dotnet style by Peter Kese

open System
open System.IO

open Microsoft.Build

open CsprojLib
open CustomConfig


let convertCsproj (nugetPkgDir:string) (outStream:Stream) (prjFPath:string) =

    let prjFileName = Path.GetFileName prjFPath
    let prjDir = Path.GetDirectoryName prjFPath
    let projectName = Path.GetFileNameWithoutExtension prjFileName
    let nugetDir = Path.GetFullPath nugetPkgDir
    let customizations = defineCustomizations prjFPath projectName
    let packageVersions = dict customizations.packageVersions
    let packageReplacements = customizations.packageReplacements |> List.map (fun (o,n,v) -> o,(n,v)) |> dict


    let mutable vs2015conversion = false
    let mutable vs2015globFixed = false

    let isIt2015styleProject node =
        (findAttr "xmlns" node) <> None && (findAttr "Sdk" node) = None


    let rewriteProjectNodeTo2017 node = 
        if isIt2015styleProject node
        then setNodeAttrs ["Sdk","Microsoft.NET.Sdk";] node
        else Old node

    let rewriteProjectReference (node:Node) :Result<Node> =
        (* <ProjectReference Include="..\ClassLibrary1\ClassLibrary1.csproj">
              <Project>{2C7DF870-5B35-49EF-963D-EE1E72C3177E}</Project>
              <Name>ClassLibrary1</Name>
           </ProjectReference> *)
        let filterChild (cn:Node) :Result<Node> = 
            match cn.name with 
                | "Project" -> Del
                | "Name" -> Del
                | _ -> Old cn
        mapImmediateChildren filterChild node

    (*   <Reference Include="MySql.Data, Version=6.9.9.0, Culture=neutral, PublicKeyToken=c5687fc88969c44d, processorArchitecture=MSIL">
            <HintPath>..\..\packages\MySql.Data.6.9.9\lib\net45\MySql.Data.dll</HintPath>
            <Private>True</Private>
         </Reference> *)

    let tryExtractPackageReferenceVersion rootNode (libName:string) :string option=

        let getNugetVersion libPath :string option =
            let absPath = Uri(Path.Combine (prjDir, libPath)).LocalPath
            let prefix = Path.Combine(nugetDir, libName)
            if absPath.ToLower().StartsWith (prefix.ToLower ())
            then
                //printfn "> %s extracted from \n> %s" absPath.[(prefix.Length)..] absPath
                let rec skipToDigits = function
                    | "" -> ""
                    | s when Char.IsDigit(s.[0]) -> s
                    | s -> skipToDigits (s.[1..])
                absPath.[(prefix.Length)..]
                    |> (fun s -> s.Split [|'/';'\\'|]) // split remainder into path elements
                    |> Array.toList
                    |> function
                        | h::t -> h.Trim [|'\\';'/';'.'|] |> skipToDigits |> Some // trim junk to get to digits
                        | _ -> Some ""
            elif absPath.ToLower().StartsWith (nugetDir.ToLower ())
            then Some ""
            else None

        rootNode.children
            |> List.map (function
                | Old node when node.name = "HintPath" -> Some node.text
                | _ -> None)
            |> List.filter (fun ch -> ch <> None) // only HintPath's text
            |> function // first (if any) should be version
                | [Some path] -> getNugetVersion path
                | [] -> None
                | _ -> failwith "unexpected multiple HintPaths"



    let rewritePackageReference (node:Node) :Result<Node> =

        match node |> findAttr "Include" with
            | Some incStr -> 
                // parse <Package Include="MySql.Data, Version=6.9.9.0, Culture=neutral, ...">
                let parseInclude () :string*string = 
                    incStr.Split(',') |> Seq.map (fun s -> s.Trim()) |> Seq.toList
                        |> function
                            | [] -> failwith "missing package name"
                            | [pkg] -> (pkg,"")
                            | pkg::tail -> 
                                tail |> List.fold ( // find "Version=6.9.9.0" in string items
                                    fun (p,v) (eq:string) -> 
                                        match eq.Split '=' with
                                            | [|"Version"; n|] -> (p,n)
                                            | _ -> (p,v)
                                    ) (pkg,"")

                // check if we have a static replacement customization
                let package, version, nugetVer, isNuget = 
                    let pkgName,pkgVer = parseInclude ()
                    match packageReplacements.TryGetValue pkgName with
                        | true, (p2,v2) -> (p2,"foo",v2,true)
                        | false, _ -> 
                            let nugetVer, isNuget = 
                                match tryExtractPackageReferenceVersion node pkgName with
                                    | Some "" -> "", true
                                    | Some ver -> ver, true
                                    | None -> "", false
                            match packageVersions.TryGetValue pkgName with
                                | true,version -> pkgName, "bar", version, true
                                | false,_ -> pkgName, pkgVer, nugetVer, isNuget

                match version,nugetVer,isNuget with
                    | "","",true -> Del
                    | "","",false -> Old node
                    | pkg,"",false -> Old node // path not pointing to nuget directory
                    | pkg,"",true -> 
                        // OH HORROR - we'll just delete stuff we can't find on root level of package cache
                        // - old style projects were referencing too much stuff anyway
                        // - in case something is actually missing just add it back manually
                        Del 
                    | _,nug,_ -> 
                        New { name="PackageReference"; attrs=["Include",package; "Version",nug;]; text=""; children=[]; path=node.path }

            | x -> unexpected node

    let insertVS2015GlobOverride node = 
        // https://docs.microsoft.com/en-us/dotnet/articles/core/tools/csproj
        if vs2015conversion && not vs2015globFixed && List.isEmpty node.attrs
        then
            vs2015globFixed <- true 
            setNodeChildren (List.append node.children
                [
                    New { name = "EnableDefaultCompileItems"; text = "false"; path = node.path; attrs = []; children = []} ;
                    //New { name = "EnableDefaultItems"; text = "false"; path = node.path; attrs = []; children = []}
                ]) node
        else Old node

    let filterProjectReferenceChildren node =
        match node.name with
            | "Project" -> Del // uuid
            | "Name" -> Del // defaults to the one in file
            | _ -> Old node

    let filterEmpty node = 
        let children = node.children |> List.filter (function | Del -> false | _ -> true)
        let { attrs=attrs; text=text; } = node
        let allowedWithAttrs = ["PropertyGroup"]
        let attrsMatch = (List.isEmpty attrs) || (allowedWithAttrs |> (List.exists (fun nm -> nm = node.name)))

        if (List.isEmpty children && text = "" && attrsMatch)
        then Del
        else Old node


    let rec rewriteNode node =
        match node.name with 
            | "Project" -> rewriteProjectNodeTo2017 node
            | "PropertyGroup" -> insertVS2015GlobOverride node
            | "ProjectReference" -> node |> mapImmediateChildren filterProjectReferenceChildren
            | "Reference" -> node |> rewritePackageReference
            | _ -> Old node

    and convertNode node :Result<Node> =
        //printfn "%A %s %A #%d %s" node.path node.name node.attrs (node.children |> List.length) node.text

        let runExternalFilters node =
            List.fold (=|>) (Old node) customizations.nodeFilters

        Old node
            =|> runExternalFilters
            =|> setNodeChildren (node.children |> (List.map (fun ch -> ch =|> convertNode)))
            =|> rewriteNode
            =|> filterEmpty




    // main: convert and return result

    eprintfn "processing %s" prjFPath

    loadCsprojFile prjFPath
        |> fun rootNode ->

            vs2015conversion <- isIt2015styleProject rootNode

            let result = convertNode rootNode

            match result with
                | New node -> 
                    // write back to file (or stdout)
                    storeCsprojFile outStream node |> ignore

                    eprintfn "project modified: %s" projectName
                | Old _ ->
                    eprintfn "no changes in: %s" projectName
                | Del ->
                    eprintfn "that's weird -- everything got cut off"
                | Err err ->
                    eprintfn "error in %s: %s" projectName err

            result // that's the conversion result


////////////////////////////////////////////////////////////////////////////////
// 
// ^^^ end of csproj conversion stuff following is parsing the args and calling it
    

let iterateOverProjectsInSolution solutionFile =
    // load solution from file and iterate over elements
    let slnObj = Construction.SolutionFile.Parse (Path.GetFullPath solutionFile)
    slnObj.ProjectsInOrder
        |> Seq.map (fun prj -> prj.AbsolutePath)

let usage = """
    {analyze|patch} pathToSolutionFile
"""    

type Error = string option

let handle sln pkgDir preview :Error =

    let stdout = Console.OpenStandardOutput()
    let absPkgDir = Path.GetFullPath (Path.Combine (Directory.GetCurrentDirectory (), pkgDir))
    printfn "nuget: %s" absPkgDir

    let processSingleCsproj csproj =

        if preview then printfn "\n--------------------------------------------------------------------------\n"
        else printfn ""

        if preview then
            convertCsproj pkgDir stdout csproj
        else
            use ms = new MemoryStream ()
            let result = convertCsproj pkgDir ms csproj
            match result with 
                | New tree ->
                    use fout = new FileStream (csproj, FileMode.Truncate)
                    ms.WriteTo(fout)
                    ms.Flush ()

                    let packagesFile = Path.Combine ((Path.GetDirectoryName csproj), "packages.config")
                    if File.Exists packagesFile
                    then File.Delete packagesFile
                    
                | _ -> ()
            result

    iterateOverProjectsInSolution sln
        |> Seq.filter (fun prj -> prj.EndsWith ".csproj")
        //|> Seq.toList |> (fun s -> s.[..10])
        |> Seq.map processSingleCsproj
        |> Seq.iter ignore
    None

[<EntryPoint>]
let main argv =

    let error = 
        match (Array.toList argv) with
            | ["analyze"; sln; pkgDir] -> handle sln pkgDir true
            | ["patch";sln; pkgDir] -> handle sln pkgDir false
            | _ -> Some usage

    error |> function
        | None -> 0
        | Some text ->
            printfn "%s" text
            1