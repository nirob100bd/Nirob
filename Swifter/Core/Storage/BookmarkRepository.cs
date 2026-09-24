namespace Swifter.Core.Storage;

public sealed class BookmarkNode
{
    public long Id { get; set; }
    public long ParentId { get; set; }
    public string Title { get; set; }
    public string Url { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Position { get; set; }
    public bool IsFolder { get; set; }
    public List<BookmarkNode> Children { get; set; } = new List<BookmarkNode>();
}

public sealed class BookmarkRepository
{
    private readonly DatabaseContext _db;

    public BookmarkRepository(DatabaseContext db)
    {
        _db = db;
        EnsureRootFolders();
    }

    private void EnsureRootFolders()
    {
        var count = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM bookmarks WHERE is_folder = 1 AND parent_id = 0"));
        if (count == 0)
        {
            _db.Execute("INSERT INTO bookmarks (parent_id, title, url, created_at, position, is_folder) VALUES (0, 'Bookmarks Bar', '', @now, 0, 1)", ("@now", DateTime.UtcNow.Ticks));
            _db.Execute("INSERT INTO bookmarks (parent_id, title, url, created_at, position, is_folder) VALUES (0, 'Other Bookmarks', '', @now, 1, 1)", ("@now", DateTime.UtcNow.Ticks));
        }
    }

    public BookmarkNode AddBookmark(string title, string url, long parentId = 0)
    {
        var pos = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(position), -1) + 1 FROM bookmarks WHERE parent_id = @p", ("@p", parentId)));
        _db.Execute("INSERT INTO bookmarks (parent_id, title, url, created_at, position, is_folder) VALUES (@p, @t, @u, @now, @pos, 0)",
            ("@p", parentId), ("@t", title ?? url), ("@u", url), ("@now", DateTime.UtcNow.Ticks), ("@pos", pos));
        var id = Convert.ToInt64(_db.Scalar("SELECT last_insert_rowid()"));
        return new BookmarkNode { Id = id, ParentId = parentId, Title = title, Url = url, CreatedAt = DateTime.UtcNow, Position = pos };
    }

    public BookmarkNode AddFolder(string title, long parentId = 0)
    {
        var pos = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(position), -1) + 1 FROM bookmarks WHERE parent_id = @p", ("@p", parentId)));
        _db.Execute("INSERT INTO bookmarks (parent_id, title, url, created_at, position, is_folder) VALUES (@p, @t, '', @now, @pos, 1)",
            ("@p", parentId), ("@t", title ?? "Folder"), ("@now", DateTime.UtcNow.Ticks), ("@pos", pos));
        var id = Convert.ToInt64(_db.Scalar("SELECT last_insert_rowid()"));
        return new BookmarkNode { Id = id, ParentId = parentId, Title = title, IsFolder = true, CreatedAt = DateTime.UtcNow, Position = pos };
    }

    public void Rename(long id, string title)
    {
        _db.Execute("UPDATE bookmarks SET title = @t WHERE id = @id", ("@t", title), ("@id", id));
    }

    public void Move(long id, long newParentId, int position = -1)
    {
        if (position < 0)
        {
            position = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(position), -1) + 1 FROM bookmarks WHERE parent_id = @p", ("@p", newParentId)));
        }
        _db.Execute("UPDATE bookmarks SET parent_id = @p, position = @pos WHERE id = @id",
            ("@p", newParentId), ("@pos", position), ("@id", id));
    }

    public void Delete(long id)
    {
        var ids = CollectDescendants(id);
        foreach (var del in ids.AsEnumerable().Reverse())
        {
            _db.Execute("DELETE FROM bookmarks WHERE id = @id", ("@id", del));
        }
    }

    private List<long> CollectDescendants(long id)
    {
        var result = new List<long> { id };
        var queue = new Queue<long>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = _db.QueryList("SELECT id FROM bookmarks WHERE parent_id = @p", r => r.GetInt64(0), ("@p", current));
            foreach (var c in children)
            {
                if (!result.Contains(c))
                {
                    result.Add(c);
                    queue.Enqueue(c);
                }
            }
        }
        return result;
    }

    public List<BookmarkNode> GetChildren(long parentId)
    {
        return _db.QueryList(
            "SELECT id, parent_id, title, url, created_at, position, is_folder FROM bookmarks WHERE parent_id = @p ORDER BY is_folder DESC, position ASC",
            Map,
            ("@p", parentId));
    }

    public List<BookmarkNode> GetTree()
    {
        var all = _db.QueryList(
            "SELECT id, parent_id, title, url, created_at, position, is_folder FROM bookmarks ORDER BY is_folder DESC, position ASC",
            Map);
        var lookup = all.ToDictionary(b => b.Id);
        var roots = new List<BookmarkNode>();
        foreach (var node in all)
        {
            if (lookup.TryGetValue(node.ParentId, out var parent)) parent.Children.Add(node);
            else roots.Add(node);
        }
        return roots;
    }

    public List<BookmarkNode> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetChildren(0);
        var q = "%" + query.Trim() + "%";
        return _db.QueryList(
            "SELECT id, parent_id, title, url, created_at, position, is_folder FROM bookmarks WHERE is_folder = 0 AND (title LIKE @q OR url LIKE @q) LIMIT @limit",
            Map,
            ("@q", q),
            ("@limit", limit));
    }

    public BookmarkNode GetById(long id)
    {
        var list = _db.QueryList(
            "SELECT id, parent_id, title, url, created_at, position, is_folder FROM bookmarks WHERE id = @id",
            Map,
            ("@id", id));
        return list.FirstOrDefault();
    }

    public bool IsBookmarked(string url)
    {
        var count = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM bookmarks WHERE url = @u AND is_folder = 0", ("@u", url)));
        return count > 0;
    }

    public void RemoveByUrl(string url)
    {
        _db.Execute("DELETE FROM bookmarks WHERE url = @u AND is_folder = 0", ("@u", url));
    }

    public int Count()
    {
        return Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM bookmarks WHERE is_folder = 0"));
    }

    private static BookmarkNode Map(System.Data.IDataReader r)
    {
        return new BookmarkNode
        {
            Id = r.GetInt64(0),
            ParentId = r.GetInt64(1),
            Title = r.GetString(2),
            Url = r.GetString(3),
            CreatedAt = new DateTime(r.GetInt64(4), DateTimeKind.Utc).ToLocalTime(),
            Position = r.GetInt32(5),
            IsFolder = r.GetInt32(6) != 0
        };
    }
}
