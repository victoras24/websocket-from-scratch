

public class HttpHandlerRequest
{
    public string Method {get; set;}
    public string Uri {get; set;}
    public string HttpVersion {get; set;}
    public Dictionary<string, string> Headers = new Dictionary<string, string>();
}