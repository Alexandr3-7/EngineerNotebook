using System;

namespace EngineerNotebook.Models
{
    public class NoteVersion
    {
        public long VersionId { get; set; }
        public long NoteId { get; set; }

        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Category { get; set; } = "Без категории";
        public string Tags { get; set; } = "";

        public DateTime SavedAt { get; set; }
    }
}
