namespace server;

public class HttpHandler()
{
    
    public HttpHandlerRequest HandleRequestHeader(string request)
    {
        HttpHandlerRequest httpRequest = new HttpHandlerRequest();
        var lines = request.Split(["\r\n", "\n"], StringSplitOptions.None);
        var firstLine = lines[0];
        var specialLine = firstLine.Split(" ");
            if (specialLine.Length >= 3)
            { 
                httpRequest.Method = specialLine[0];
                httpRequest.Uri =  specialLine[1];
                httpRequest.HttpVersion =  specialLine[2];
            }
            else
            {
                Console.WriteLine("Invalid request line");
            }
            
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue; 

            var headerParts = lines[i].Split(':', 2); 
            if (headerParts.Length == 2)
            {
                var key = headerParts[0].Trim();
                var value = headerParts[1].Trim();
                httpRequest.Headers.Add(key.ToLower(), value);
            }
        }
        return httpRequest;
    }
}