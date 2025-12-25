using System;

namespace EngineerNotebook.Models
{
    public class Note
    {
        public long Id { get; set; }

        public string Title { get; set; } = "";
        public string Content { get; set; } = "";

        public string Category { get; set; } = "Без категории";
        public string Tags { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
