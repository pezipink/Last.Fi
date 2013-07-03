module LastFi.Api

open System
open System.IO
open System.Security.Cryptography
open System.Net
open System.Web
open System.Xml
open System.Text

let get (doc:XmlDocument) name = doc.GetElementsByTagName(name).[0].InnerText
let getN (node:XmlNode) name = (node:?>XmlElement).GetElementsByTagName(name).[0].InnerText

let MD5 (input:string) = 
    use prov = new MD5CryptoServiceProvider()
    (StringBuilder(),prov.ComputeHash(Encoding.ASCII.GetBytes input))
     ||> Array.fold(fun sb b -> sb.Append(b.ToString("x2").ToLower())) |> fun sb -> sb.ToString()

type Request(meth,session,parameters:Map<string,string>) =
    let root =  "http://ws.audioscrobbler.com/2.0/"
    let parameters =
        parameters |> Map.add "method" meth |> Map.add "api_key" session.ApiKey
        |> fun p ->            
            match session.Key with
            | Some(key) -> p |> Map.add "sk" key // add the session key in if it exists
            | None -> p
            |> fun p -> p |> Map.add "api_sig"  // create the request token from all the parameters
                         ((StringBuilder(),p)
                         ||> Map.fold(fun sb key value -> sb.Append(key).Append(value))
                         |> fun sb -> MD5 (sb.Append(session.ApiSecret).ToString()))
        
    member __.Execute() = 
        let bytes =                      
            (StringBuilder(),parameters)
            ||> Map.fold(fun sb key value -> sb.Append(HttpUtility.UrlEncode key).Append("=").Append(HttpUtility.UrlEncode value).Append("&"))
            |> fun sb -> sb.ToString(0,sb.Length-1) |> Encoding.ASCII.GetBytes
        System.Net.ServicePointManager.Expect100Continue <- false
        let req = WebRequest.Create(root) :?> HttpWebRequest
        req.ContentLength <- int64 bytes.Length
        req.UserAgent <- "Last-Fi"
        req.ContentType <- "application/x-www-form-urlencoded"
        req.Method <- "POST"
        req.Headers.["Accept-Charset"] <- "utf-8"
        let stream = req.GetRequestStream()
        stream.Write(bytes,0,bytes.Length)
        stream.Close()
        let resp = try req.GetResponse() :?> HttpWebResponse
                   with | :? WebException as e -> e.Response :?> HttpWebResponse
        let doc = XmlDocument()
        doc.Load(resp.GetResponseStream())
        doc 

and Session = { ApiKey : string; ApiSecret : string; Key : string option; }
    with 
        static member Authenticate(user,password,key,secret) =
            let res = Request("auth.getMobileSession",{ApiKey=key;ApiSecret=secret;Key=None},
                                [("username",user);("authToken",MD5 (user + (MD5 password)))] |> Map.ofList).Execute()
            {ApiKey=key;ApiSecret=secret;Key=Some(res.GetElementsByTagName("key").[0].InnerText) }
        
type Track = { Artist : string; AlbumTitle : string; Title : string; StreamPath : string }

type Artist = { Name : string; Bio: string; Image : string } 
    with 
        static member GetInfo(name,session) =
            let res = Request("artist.getInfo",session,[("artist",name);"lang","en"] |> Map.ofList).Execute()
            let content = res.GetElementsByTagName("bio").[0].SelectSingleNode("content").InnerText
            let image = res.GetElementsByTagName("artist").[0].SelectNodes("image")
                        |> Seq.cast<XmlNode> |> Seq.toList |> List.rev |> List.head
            { Name = get res "name"; Bio = content.Trim(); Image = image.InnerText }

type Radio = { Name : string; Session : Session } 
    with 
        static member Tune(stationUri,session) = 
            let res = Request("radio.tune",session,[("station",stationUri)] |> Map.ofList).Execute()
            { Name = get res "name"; Session = session }
        member r.GetTracks() = 
            let res = Request("radio.getPlaylist",r.Session,Map.empty).Execute()
            res.GetElementsByTagName("track") |> Seq.cast<XmlNode>
            |> Seq.map(fun n -> {Artist = getN n "creator"; AlbumTitle = getN n "album"; Title = getN n "title"; StreamPath = getN n "location"})
            |> Seq.toList
