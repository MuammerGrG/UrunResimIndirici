using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UrunMotor;

/// <summary>
/// Arayüz (Electron) ile satır satır JSON konuşan motor.
///   istek : {"id":1,"komut":"ayristir","arg":{...}}
///   cevap : {"id":1,"tamam":true,"sonuc":...}  /  {"id":1,"tamam":false,"hata":"...","kod":"anahtar"}
///   olay  : {"olay":"log","id":1,"metin":"..."}  {"olay":"urun",...}  {"olay":"ilerleme",...}
/// </summary>
public static class Program
{
    public const string Surum = "4.1.1";

    static readonly object YazKilit = new();
    static TextWriter _cikis;
    static readonly ConcurrentDictionary<long, CancellationTokenSource> Isler = new();
    public static readonly JsonSerializerOptions Js = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static void Gonder(object o)
    {
        var s = o is JsonNode n ? n.ToJsonString(Js) : JsonSerializer.Serialize(o, Js);
        lock (YazKilit) { var w = _cikis ?? Console.Out; w.WriteLine(s); w.Flush(); }
    }

    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (args.Length > 0 && args[0] == "--surum") { Console.WriteLine(Surum); return 0; }

        _cikis = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
        var giris = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        Gonder(new { olay = "hazir", surum = Surum });
        var gorevler = new List<Task>();
        string satir;
        while ((satir = await giris.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(satir)) continue;
            JsonNode istek;
            try { istek = JsonNode.Parse(satir); }
            catch (JsonException) { Gonder(new { olay = "log", id = 0, metin = "Motor: bozuk istek atlandı." }); continue; }
            var id = istek?["id"]?.GetValue<long>() ?? 0;
            var komut = istek?["komut"]?.GetValue<string>() ?? "";
            var arg = istek?["arg"] as JsonObject ?? new JsonObject();
            if (komut == "iptal")
            {
                var hedef = arg["id"]?.GetValue<long>() ?? 0;
                if (Isler.TryGetValue(hedef, out var c)) c.Cancel();
                Gonder(new { id, tamam = true, sonuc = (object)null });
                continue;
            }
            if (komut == "kapat") break;
            var cts = new CancellationTokenSource();
            Isler[id] = cts;
            gorevler.Add(Task.Run(() => Calistir(id, komut, arg, cts.Token)));
            gorevler.RemoveAll(g => g.IsCompleted);
        }
        // Giriş kapandı: arayüz kapanmıştır, işleri durdur. (Denemede MOTOR_BEKLE=1 ise bitmelerini bekle.)
        if (Environment.GetEnvironmentVariable("MOTOR_BEKLE") == "1")
            try { await Task.WhenAll(gorevler).WaitAsync(TimeSpan.FromSeconds(180)); } catch { }
        foreach (var c in Isler.Values) c.Cancel();
        try { await Task.WhenAll(gorevler).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        return 0;
    }

    static async Task Calistir(long id, string komut, JsonObject arg, CancellationToken t)
    {
        var ctx = new Is(id, t);
        try
        {
            var sonuc = await Komutlar.Yurut(komut, arg, ctx);
            Gonder(new JsonObject { ["id"] = id, ["tamam"] = true, ["sonuc"] = sonuc });
        }
        catch (OperationCanceledException) when (t.IsCancellationRequested)
        {
            Gonder(new { id, tamam = false, hata = "İşlem durduruldu.", kod = "iptal" });
        }
        catch (Yapay.AnahtarHatasi e) { Gonder(new { id, tamam = false, hata = e.Message, kod = "anahtar" }); }
        catch (HataMesaji e) { Gonder(new { id, tamam = false, hata = e.Message, kod = "" }); }
        catch (Exception e)
        {
            var m = e is IOException or UnauthorizedAccessException ? e.Message : $"{e.GetType().Name}: {e.Message}";
            Gonder(new { id, tamam = false, hata = m, kod = "", ayrinti = e.ToString() });
        }
        finally
        {
            if (Isler.TryRemove(id, out var c)) c.Dispose();
        }
    }
}

/// <summary>Bir isteğin bağlamı: log, olay ve iptal.</summary>
public sealed class Is
{
    public readonly long Id;
    public readonly CancellationToken T;
    public Is(long id, CancellationToken t) { Id = id; T = t; }
    public void Log(string m) => Program.Gonder(new { olay = "log", id = Id, metin = m });
    public void Olay(string ad, JsonObject veri)
    {
        veri["olay"] = ad;
        veri["id"] = Id;
        Program.Gonder(veri);
    }
}
