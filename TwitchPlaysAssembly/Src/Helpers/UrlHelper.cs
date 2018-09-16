using System.Text.RegularExpressions;
using UnityEngine;

public class UrlHelper : MonoBehaviour
{
	public static UrlHelper Instance;

	private void Awake()
	{
		Instance = this;
	}

	public void ChangeMode(bool toShort)
	{
		TwitchPlaySettings.data.LogUploaderShortUrls = toShort;
		TwitchPlaySettings.WriteDataToFile();
	}

	public bool ToggleMode()
	{
		TwitchPlaySettings.data.LogUploaderShortUrls = !TwitchPlaySettings.data.LogUploaderShortUrls;
		TwitchPlaySettings.WriteDataToFile();
		return TwitchPlaySettings.data.LogUploaderShortUrls;
	}

	private string Escape(string toEscape)
	{
		return Regex.Replace(toEscape, @"[^\w%]", m => "%" + ((int)m.Value[0]).ToString("X2"));
	}

	public string LogAnalyserFor(string url)
	{
		return string.Format(TwitchPlaySettings.data.AnalyzerUrl + (url.StartsWith("https://ktane.timwi.de") ? "#file={0}" : "#url={0}"), url);
	}

	public string CommandReference => TwitchPlaySettings.data.LogUploaderShortUrls ? "https://goo.gl/rQUH8y" : "https://github.com/samfun123/KtaneTwitchPlays/wiki/Commands";

	public string ManualFor(string module, string type = "html", bool useVanillaRuleModifier = false)
	{
		return string.Format(TwitchPlaySettings.data.RepositoryUrl + "{0}/{1}.{2}{3}", type.ToUpper(), Escape(module), type, (useVanillaRuleModifier && type.Equals("html")) ? $"#{VanillaRuleModifier.GetRuleSeed()}" : "");
	}

	public string VanillaManual = "http://www.bombmanual.com/";
}
