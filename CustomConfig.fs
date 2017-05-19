
module CustomConfig

open CsprojLib


let defineCustomizations (projectFilePath: string) (projectName: string) :Customizations = 


    let replacePackageVersions = [
        // package,                         new version (or delete if "")
        "DotNetOpenAuth.OAuth2",            ""
        "Microsoft.IdentityModel.Clients.ActiveDirectory", ""
        "FluentAssertions.Core",            "" // sub-module
        "Antlr3.Runtime",                   "3.5.1"
        "Microsoft.Threading.Tasks.Extensions", ""
        ]


    let replacePackagesWithOtherPackages = [
        // old package,                     new package,                version
        "nunit.framework",                  "NUnit",                    "3.6.1"
        "Zlib.Portable",                    "Zlib.Portable.Signed",     "1.11.0"
        "CommandLine",                      "CommandLineParser",        "1.9.71"
        "Aspose.Slides",                    "Aspose.Slides.NET",        "17.2"
        "SwaggerAPIDocumentation",          "SwaggerAPIDocumentation.Mvc4","1.1.5"
        "Couchbase.NetClient",              "CouchbaseNetClient",       "2.4.5"
        ]



    let setTargetFrameworkVersion node =
        match node.name with 
            // | "TargetFramework" // uncomment this if you wish to change target framework on alredy converted projects
            | "TargetFrameworkVersion" -> 
                Old node // return the old node, but first make some changes on it
                    =|> setNodeName "TargetFramework" 
                    =|> setNodeText "net462"
            | _ -> Old node
    

    let customNodeFilters node = 
        // simple matching of xml elements by <Name> 
        match node.name with 
            // delete random xml nodes appearing in our projects
            | "BootstrapperPackage" -> Del // seen in unit-test project

            | "OldToolsVersion" -> Del
            | "StyleCopTreatErrorsAsWarnings" -> Del
            | "RunCodeAnalysis" -> Del
            | "PlatformTarget" -> Del
            //| "AutoGenerateBindingRedirects" -> Del

            // our debug stuff
            | "TreatWarningsAsErrors" -> Del 
            | "UseVSHostingProcess" -> Del 

            // project reference, package reference (include this depending on your environment)
            | "Private" -> Del

            | "AspNetCompiler" -> Del

            | "Choose" -> Del // apparently ok to delete -- they supposedly appear in unit-test projects

            | _ -> Old node // all other cases -> leave unchanged (Old) node


    let aBitMoreComplexNodeFilters node =
        let { name = name; text = text; attrs = attrs } = node

        match name, attrs, text with 
            // delete references to packages.config:   <None Include="packages.config" />
            | "None", ["Include","packages.config"],_ -> Del
            | "Content", ["Include","packages.config"],_ -> Del

            // delete other parametrized stuff
            | "IsCodedUITest", _, "False"       -> Del
            | "ReferencePath", _,s when s.StartsWith "$(ProgramFiles)" -> Del
            | "VSToolsPath", ["Condition","'$(VSToolsPath)' == ''"],s when s.StartsWith "$(MSBuildExtensionsPath" -> Del
            | "VisualStudioVersion", _, _       -> Del

            | "Prefer32Bit", _, "false"         -> Del
            | "SignAssembly", _, "false"        -> Del
            | "ConsolePause", _, "false"        -> Del
            | "AllowUnsafeBlocks", _, "false"   -> Del

            // we never had any Production builds, so just delete all production configuration sections
            // <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
            | "PropertyGroup", ["Condition",str], any when str.Contains "Release|AnyCPU" -> Del

            // exclude default assembly info since it will be auto-discovered
            | "Compile", ["Include",@"Properties\AssemblyInfo.cs"],_ -> Del
            | "Folder", ["Include",@"Properties\"], _ -> Del


            | _ -> Old node // all other cases -> leave unchanged (Old) node
            
    let filterProjectImports node :Result<Node> = 
        // <Import Project="$(MSBuildExtensionsPath)..." ...>
        if node.name = "Import"
        then 
            match node |> findAttr "Project" with
                | Some str when str.StartsWith("$(MSBuildExtensionsPath)") -> Del
                | Some str when str.StartsWith("$(MSBuildToolsPath)") -> Del
                | Some str when str.StartsWith("$(MSBuildBinPath)") -> Del
                | Some str when str.StartsWith("$(VSToolsPath)") -> Del
                | Some otherStr -> Old node
                | None -> Old node
        else Old node

    let filterGeneratedAssemplyInfos node =
        let { name = name; text = text; attrs = attrs } = node
        // <Compile Include="..\..\..\..\..\build\GeneratedAssemblyInfo.cs">
        if name = "Compile"
        then
            match attrs with
                | ["Include",path] when path.EndsWith @"build\GeneratedAssemblyInfo.cs" -> Del
                | _ -> Old node // all other cases -> leave unchanged (Old) node
        else Old node

    let filterDefaults node =
        // these are supposedly safe to delete
        match (node.name, node.attrs, node.text) with 
            | "Configuration",["Condition"," '$(Configuration)' == '' "],"Debug" -> Del
            | "Platform",["Condition"," '$(Platform)' == '' "],"AnyCPU" -> Del
            | "OutputType",_,"Library" -> Del
            | "AppDesignerFolder",_,"Properties" -> Del
            | "Optimize",_,"false" -> Del
            | "ProjectGuid",_,_ -> Del
            | "ProjectTypeGuids",_,_ -> Del
            | "FileAlignment",_,_ -> Del
            | "DebugSymbols",_,"true" -> Del
            | "DebugType",_,"full" -> Del
            | "OutputPath",_,@"bin\Debug\" -> Del  // @ helps avoiding having to escape \ -> \\
            | "OutputPath",_,@"bin\Debug" -> Del
            | "DefineConstants",_,"DEBUG;TRACE" -> Del
            | "DefineConstants",_,"TRACE;DEBUG" -> Del
            | "DefineConstants",_,"DEBUG" -> Del
            | "DefineConstants",_,"DEBUG;" -> Del
            | "ErrorReport",_,"prompt" -> Del
            | "WarningLevel",_,"4" -> Del
            | "NuGetPackageImportStamp",_,_ -> Del
            | "TargetFrameworkProfile",_,_ -> Del
            | "TestProjectType",_,"UnitTest" -> Del

            | _ -> Old node

    let filterThingsWhereDefaultTextIsProjectName node = 
        match (node.name, node.text) with 
            | "RootNamespace", projectName -> Del
            | "AssemblyName", projectName -> Del
            | "PackageId", projectName -> Del
            | _ -> Old node


    /////////////////////////////////////////
    //
    // return a combination of the above

    {
        nodeFilters = 
            [
                setTargetFrameworkVersion
                customNodeFilters
                filterGeneratedAssemplyInfos
                aBitMoreComplexNodeFilters
                filterDefaults
                filterProjectImports
                filterThingsWhereDefaultTextIsProjectName
            ]

        packageVersions = replacePackageVersions

        packageReplacements = replacePackagesWithOtherPackages
    }