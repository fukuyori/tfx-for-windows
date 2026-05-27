using System.IO;
using System.Text;

namespace Tfx;

internal sealed record TextPreviewResult(string Text, string EncodingName, string NewlineName);

internal static class TextPreviewReader
{
    private const int MaxPreviewBytes = 256 * 1024;

    static TextPreviewReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TextPreviewResult Read(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Stream the first MaxPreviewBytes only. The old `File.ReadAllBytes` +
        // slice approach loaded the entire file into memory before discarding
        // the tail, so a 10 GB .log would OOM the app just by being clicked.
        byte[] previewBytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var toRead = (int)Math.Min(stream.Length, MaxPreviewBytes);
            previewBytes = new byte[toRead];
            var offset = 0;
            while (offset < toRead)
            {
                var read = stream.Read(previewBytes, offset, toRead - offset);
                if (read <= 0) break;
                offset += read;
            }
            if (offset != toRead)
            {
                Array.Resize(ref previewBytes, offset);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var encoding = DetectEncoding(previewBytes);
        var text = encoding.GetString(previewBytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return new TextPreviewResult(text, GetDisplayName(encoding), DetectNewline(text));
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
            }
        }

        if (LooksLikeIso2022Jp(bytes))
        {
            return StrictEncoding("iso-2022-jp");
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (CanDecode(bytes, utf8))
        {
            return utf8;
        }

        var eucJp = StrictEncoding("euc-jp");
        var shiftJis = StrictEncoding("shift_jis");
        var eucScore = ScoreDecodedJapanese(bytes, eucJp);
        var shiftJisScore = ScoreDecodedJapanese(bytes, shiftJis);

        if (eucScore.Valid && (!shiftJisScore.Valid || eucScore.Score > shiftJisScore.Score))
        {
            return eucJp;
        }

        if (shiftJisScore.Valid)
        {
            return shiftJis;
        }

        if (eucScore.Valid)
        {
            return eucJp;
        }

        return Encoding.Default;
    }

    private static Encoding StrictEncoding(string name) =>
        Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static bool CanDecode(byte[] bytes, Encoding encoding)
    {
        try
        {
            encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static (bool Valid, int Score) ScoreDecodedJapanese(byte[] bytes, Encoding encoding)
    {
        try
        {
            var text = encoding.GetString(bytes);
            var japanese = 0;
            var controls = 0;
            foreach (var c in text)
            {
                if (IsJapanese(c))
                {
                    japanese++;
                }
                else if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
                {
                    controls++;
                }
            }

            return (true, japanese - controls * 4);
        }
        catch (DecoderFallbackException)
        {
            return (false, int.MinValue);
        }
    }

    private static bool LooksLikeIso2022Jp(byte[] bytes)
    {
        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] != 0x1B)
            {
                continue;
            }

            if (bytes[i + 1] == 0x24 && (bytes[i + 2] is 0x40 or 0x42))
            {
                return true;
            }

            if (bytes[i + 1] == 0x28 && (bytes[i + 2] is 0x42 or 0x49 or 0x4A))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJapanese(char c) =>
        c is >= '\u3040' and <= '\u30FF' ||
        c is >= '\u3400' and <= '\u9FFF' ||
        c is >= '\uF900' and <= '\uFAFF';

    private static string DetectNewline(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = 0;
        kinds += crlf > 0 ? 1 : 0;
        kinds += lf > 0 ? 1 : 0;
        kinds += cr > 0 ? 1 : 0;

        return kinds switch
        {
            0 => Loc.T("None"),
            > 1 => Loc.F("Mixed (CRLF: {0}, LF: {1}, CR: {2})", crlf, lf, cr),
            _ when crlf > 0 => $"CRLF ({crlf})",
            _ when lf > 0 => $"LF ({lf})",
            _ => $"CR ({cr})"
        };
    }

    private static string GetDisplayName(Encoding encoding)
    {
        var webName = encoding.WebName.ToLowerInvariant();
        return webName switch
        {
            "utf-8" => encoding.GetPreamble().Length > 0 ? "UTF-8 (BOM)" : "UTF-8 (no BOM)",
            "shift_jis" => "Shift_JIS",
            "euc-jp" => "EUC-JP",
            "iso-2022-jp" => "ISO-2022-JP (JIS)",
            "utf-16" => "UTF-16 LE (BOM)",
            "utf-16be" => "UTF-16 BE (BOM)",
            _ => encoding.EncodingName
        };
    }
}
