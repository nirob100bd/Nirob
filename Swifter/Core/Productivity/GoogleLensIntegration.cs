using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Swifter.Core.Productivity;

public sealed class GoogleLensIntegration
{
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public bool IsBusy { get; private set; }
    public event Action<string> LensOpened;
    public event Action<string> Error;

    public string BuildLensUrl(string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return "https://lens.google.com/";
        return "https://lens.google.com/uploadbyurl?url=" + Uri.EscapeDataString(pageUrl);
    }

    public string BuildLensPasteUrl()
    {
        return "https://lens.google.com/";
    }

    public async Task<string> UploadImageForLensAsync(string imagePath, string pageUrl = null)
    {
        if (IsBusy) throw new InvalidOperationException("Lens upload already in progress");
        IsBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            return await UploadBytesForLensAsync(bytes, Path.GetFileName(imagePath), pageUrl).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> UploadBytesForLensAsync(byte[] imageBytes, string fileName, string pageUrl = null)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Error?.Invoke("Empty image data");
            return null;
        }
        if (imageBytes.Length > 20 * 1024 * 1024)
        {
            Error?.Invoke("Image exceeds 20 MB limit");
            return null;
        }
        IsBusy = true;
        try
        {
            using var content = new MultipartFormDataContent("----SwifterBoundary" + Guid.NewGuid().ToString("N"));
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(DetectMime(fileName));
            content.Add(imageContent, "encoded_image", fileName ?? "capture.png");
            if (!string.IsNullOrEmpty(pageUrl))
            {
                content.Add(new StringContent(pageUrl), "image_url");
            }
            content.Add(new StringContent("btnG"), "action");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://lens.google.com/v3/upload")
            {
                Content = content
            };
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.Referrer = string.IsNullOrEmpty(pageUrl) ? null : new Uri(pageUrl);
            using var resp = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var finalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? "https://lens.google.com/";
            if (resp.IsSuccessStatusCode)
            {
                LensOpened?.Invoke(finalUrl);
                return finalUrl;
            }
            Error?.Invoke("Lens returned " + (int)resp.StatusCode);
            return BuildLensPasteUrl();
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
            return BuildLensPasteUrl();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> ReverseImageSearchAsync(byte[] imageBytes)
    {
        return await UploadBytesForLensAsync(imageBytes, "search.png").ConfigureAwait(false);
    }

    public string BuildBingVisualSearchUrl(string imageUrl)
    {
        return "https://www.bing.com/images/search?view=detailv2&iss=sbi&form=SBIVSP&q=imgurl:" + Uri.EscapeDataString(imageUrl ?? "");
    }

    public static string DetectMime(string fileName)
    {
        var ext = (Path.GetExtension(fileName) ?? ".png").ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            _ => "image/png"
        };
    }

    public static byte[] ComputeImageHash(byte[] data)
    {
        return SHA256.HashData(data);
    }

    public string GetLensJsHook()
    {
        return @"
(function(){
if(window.__swifter_lens__)return;window.__swifter_lens__=true;
document.addEventListener('contextmenu',function(e){
var t=e.target;
if(t&&(t.tagName==='IMG'||t.tagName==='CANVAS')){
try{
window.chrome&&chrome.webview&&chrome.webview.postMessage({type:'swifter-lens-target',src:t.src||'',x:e.clientX,y:e.clientY});
}catch(err){}
}
},true);
})();
";
    }
}
