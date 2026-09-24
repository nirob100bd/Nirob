using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Swifter.Core.Storage;

public sealed class SavedPassword
{
    public long Id { get; set; }
    public string Origin { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public int Strength { get; set; }
}

public sealed class AutofillTemplate
{
    public long Id { get; set; }
    public string ProfileName { get; set; }
    public string FieldName { get; set; }
    public string FieldValue { get; set; }
    public string FieldType { get; set; }
}

public sealed class PasswordAutofillManager
{
    private readonly DatabaseContext _db;
    private readonly byte[] _key;
    private readonly object _lock = new object();

    public PasswordAutofillManager(DatabaseContext db, string machineKey)
    {
        _db = db;
        _key = DeriveKey(machineKey);
    }

    private static byte[] DeriveKey(string secret)
    {
        using var pbkdf = new Rfc2898DeriveBytes(secret ?? "swifter", Encoding.UTF8.GetBytes("SwifterSalt$2026"), 100_000, HashAlgorithmName.SHA256);
        return pbkdf.GetBytes(32);
    }

    private (byte[] iv, byte[] cipher, byte[] tag) Encrypt(string plain)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plain ?? "");
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
        {
            aes.Encrypt(iv, plainBytes, cipher, tag);
        }
        return (iv, cipher, tag);
    }

    private string Decrypt(byte[] iv, byte[] cipher, byte[] tag)
    {
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, 16))
        {
            aes.Decrypt(iv, cipher, tag, plain);
        }
        return Encoding.UTF8.GetString(plain);
    }

    public void SavePassword(string origin, string username, string password, string note = null)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(username)) return;
        lock (_lock)
        {
            var (iv, cipher, tag) = Encrypt(password);
            var now = DateTime.UtcNow.Ticks;
            _db.Execute(@"
INSERT INTO passwords (origin, username, password_enc, password_iv, password_tag, note, created_at, updated_at, last_used)
VALUES (@o, @u, @e, @i, @t, @n, @now, @now, 0)
ON CONFLICT(origin, username) DO UPDATE SET
  password_enc = excluded.password_enc,
  password_iv = excluded.password_iv,
  password_tag = excluded.password_tag,
  note = excluded.note,
  updated_at = excluded.updated_at",
                ("@o", origin),
                ("@u", username),
                ("@e", cipher),
                ("@i", iv),
                ("@t", tag),
                ("@n", note ?? ""),
                ("@now", now));
        }
    }

    public List<SavedPassword> GetAll()
    {
        return _db.QueryList(
            @"SELECT id, origin, username, password_enc, password_iv, password_tag, note, created_at, updated_at, last_used
              FROM passwords ORDER BY origin, username",
            MapPassword);
    }

    public List<SavedPassword> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        var q = "%" + query.Trim() + "%";
        return _db.QueryList(
            @"SELECT id, origin, username, password_enc, password_iv, password_tag, note, created_at, updated_at, last_used
              FROM passwords WHERE origin LIKE @q OR username LIKE @q ORDER BY origin",
            MapPassword,
            ("@q", q));
    }

    public SavedPassword Get(string origin, string username)
    {
        var list = _db.QueryList(
            @"SELECT id, origin, username, password_enc, password_iv, password_tag, note, created_at, updated_at, last_used
              FROM passwords WHERE origin = @o AND username = @u",
            MapPassword,
            ("@o", origin),
            ("@u", username));
        return list.FirstOrDefault();
    }

    public string GetPasswordFor(string origin, string username)
    {
        var entry = Get(origin, username);
        if (entry == null)
        {
            entry = GetAll().FirstOrDefault(p =>
                origin != null && p.Origin != null &&
                (p.Origin.Contains(origin, StringComparison.OrdinalIgnoreCase) || origin.Contains(p.Origin, StringComparison.OrdinalIgnoreCase)) &&
                (username == null || p.Username == username));
        }
        return entry?.Password;
    }

    public void DeletePassword(long id)
    {
        _db.Execute("DELETE FROM passwords WHERE id = @id", ("@id", id));
    }

    public void TouchUsed(long id)
    {
        _db.Execute("UPDATE passwords SET last_used = @now WHERE id = @id", ("@now", DateTime.UtcNow.Ticks), ("@id", id));
    }

    private SavedPassword MapPassword(System.Data.IDataReader r)
    {
        var id = r.GetInt64(0);
        var origin = r.GetString(1);
        var username = r.GetString(2);
        var cipher = (byte[])r["password_enc"];
        var iv = (byte[])r["password_iv"];
        var tag = (byte[])r["password_tag"];
        string password = "";
        try
        {
            lock (_lock) password = Decrypt(iv, cipher, tag);
        }
        catch
        {
            password = "";
        }
        return new SavedPassword
        {
            Id = id,
            Origin = origin,
            Username = username,
            Password = password,
            Note = r.IsDBNull(6) ? "" : r.GetString(6),
            CreatedAt = new DateTime(r.GetInt64(7), DateTimeKind.Utc).ToLocalTime(),
            UpdatedAt = new DateTime(r.GetInt64(8), DateTimeKind.Utc).ToLocalTime(),
            LastUsed = r.GetInt64(9) == 0 ? default : new DateTime(r.GetInt64(9), DateTimeKind.Utc).ToLocalTime(),
            Strength = ScoreStrength(password)
        };
    }

    public void SaveAutofill(AutofillTemplate template)
    {
        if (template == null || string.IsNullOrEmpty(template.FieldName)) return;
        var (iv, cipher, tag) = Encrypt(template.FieldValue ?? "");
        _db.Execute(
            "INSERT INTO autofill (profile_name, field_name, field_value_enc, field_iv, field_tag, field_type, created_at) VALUES (@p, @n, @e, @i, @t, @ft, @now)",
            ("@p", template.ProfileName ?? "default"),
            ("@n", template.FieldName),
            ("@e", cipher),
            ("@i", iv),
            ("@t", tag),
            ("@ft", template.FieldType ?? "text"),
            ("@now", DateTime.UtcNow.Ticks));
    }

    public List<AutofillTemplate> GetAutofill(string profile = "default")
    {
        return _db.QueryList(
            "SELECT id, profile_name, field_name, field_value_enc, field_iv, field_tag, field_type, created_at FROM autofill WHERE profile_name = @p",
            r =>
            {
                var iv = (byte[])r["field_iv"];
                var cipher = (byte[])r["field_value_enc"];
                var tag = (byte[])r["field_tag"];
                var value = "";
                try { lock (_lock) value = Decrypt(iv, cipher, tag); } catch { }
                return new AutofillTemplate
                {
                    Id = r.GetInt64(0),
                    ProfileName = r.GetString(1),
                    FieldName = r.GetString(2),
                    FieldValue = value,
                    FieldType = r.IsDBNull(6) ? "text" : r.GetString(6)
                };
            },
            ("@p", profile));
    }

    public static int ScoreStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
        return Math.Min(4, score / 2 + (score >= 5 ? 1 : 0));
    }

    public void ExportJson(string path, string exportPassword)
    {
        var all = GetAll();
        var payload = all.Select(p => new { p.Origin, p.Username, p.Password, p.Note }).ToList();
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var (iv, cipher, tag) = Encrypt(json);
        var envelope = new
        {
            Format = "swifter-passwords-v1",
            IV = Convert.ToBase64String(iv),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(envelope));
    }
}
