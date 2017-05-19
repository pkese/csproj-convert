
module CsprojLib

open System.IO

open System.Xml
open System.Xml.Linq


type Result<'T> =
    | Old of 'T         // unmodified node
    | New of 'T         // modified node
    | Err of string
    | Del

type Attribute = string * string

// this represents an xml node: <name [(attr,ibute);...]>text or <...children> </name>
type Node = {
    name: string
    attrs: Attribute list
    text: string
    children: Result<Node> list
    path: string list // where was the node initially found (reverse): [Reference,ItemGroup,Project]
}

type Customizations = {
    nodeFilters: (Node -> Result<Node>) list
    packageVersions: (string * string) list
    packageReplacements: (string * string * string) list
}

/// pass result over to next node but only if result is Old/New ;; skip if Deleted or Error
let (=|>) (res:Result<'T>) (fn:'T -> Result<'T>) = 
    match res with
        | Old x -> fn x
        | New x -> 
             fn x |> function
                | Old z -> New z
                | y -> y
        | Err e -> Err e
        | Del -> Del


/// report an error
let unexpected node :Result<Node> =
    printfn "\n\n\n\nUnexpected node %s in %A\n\n\n" node.name (List.rev node.path)
    Err (sprintf "Unexpected node %s at %A" node.name (List.rev node.path))


/// get children that were not deleted
let getRemainingChildren (node:Node) = 
    node.children |> List.filter (fun child -> child <> Del)

/// return a node with a new name
let setNodeName name node =
    if node.name = name then Old node
    else New { name = name; attrs = node.attrs; text = node.text; children = node.children; path = node.path }
let setNodeText text node =
    if node.text = text then Old node
    else New { name = node.name; attrs = node.attrs; text = text; children = node.children; path = node.path }
let setNodeAttrs attrs node =
    if node.attrs = attrs then Old node
    else New { name = node.name; attrs = attrs; text = node.text; children = node.children; path = node.path }
let setNodeChildren (children:Result<Node> list) (node:Node) :Result<Node> =
    let allChildren = 
        getRemainingChildren node
            |> List.fold (fun acc ch -> acc =|> (fun _ -> ch)) (Old node)
    //printfn "children %A %s %d %A" (List.rev node.path) node.name (List.length children) allChildren
    let newNode = New { name = node.name; attrs = node.attrs; text = node.text; children = children; path = node.path }
    match allChildren with
        | Old _ -> 
            if children <> node.children 
            then newNode
            else Old node
        | New _ -> newNode
        | x -> x

/// map direct descendants if node if not deleted
let mapImmediateChildren fn (node:Node) :Result<Node> =
    let newChildren = 
        node.children |> List.map (fun ch -> ch =|> fn)
    setNodeChildren newChildren node


/// find attribute value on a node
let findAttr name node :string option=
    node.attrs 
        |> List.filter (fun (n,_) -> n = name)
        |> List.map snd
        |> function
            | [] -> None
            | [h] -> Some h
            | x -> failwith "each attribute in xml node should only appear once"


/// transform XML tree into a tree of Nodes
let rec unwrapXmlElement path (el:XElement) : Node =
    let name = el.Name.LocalName
    let text =
        el.Nodes()
        |> Seq.filter (fun x -> x.NodeType = XmlNodeType.Text || x.NodeType = XmlNodeType.CDATA) 
        |> Seq.map (function | :? XText as x -> x.Value | _ -> null)
        |> Seq.filter (fun x -> x <> null)
        |> Seq.fold (+) ""
    //printfn "-> %A %s '%s'" (path|>List.rev) name text
    {
        name = name
        attrs = el.Attributes () |> Seq.map (fun a -> (a.Name.LocalName, a.Value.ToString ())) |> Seq.toList
        text = text
        children = el.Elements () |> Seq.map (unwrapXmlElement (name::path)) |> Seq.map Old |> Seq.toList 
        path = path
    }

/// transform node into XML
let rec wrapNodeToXml node :XElement =
    let name = XName.Get(node.name)
    let attrs = node.attrs |> List.map (fun (name,value) -> XAttribute(XName.Get name,value) :> XObject) 
    let text = if node.text <> "" then [XText(node.text) :> XObject] else []
    let children = 
        node.children 
            |> List.filter (function 
                | Old n | New n -> true
                | _ -> false)
            |> List.map (function
                | Old node | New node -> wrapNodeToXml node :> XObject
                | _ -> failwith "this shouldn't happen")
    let contents = (List.append attrs children) |> List.append text
    XElement(name,contents)

let storeCsprojFile (outStream:Stream) (node:Node) =
    let xml = wrapNodeToXml node

    let settings = XmlWriterSettings ()
    settings.OmitXmlDeclaration <- true
    settings.Indent <- true
    let writer = XmlWriter.Create(outStream, settings);

    xml.Save(writer)
    writer.Flush ()

    node

let loadCsprojFile (filePath:string) =
    XElement.Load filePath
        |> unwrapXmlElement []
