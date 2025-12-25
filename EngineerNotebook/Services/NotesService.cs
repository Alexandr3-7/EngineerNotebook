using System;
using System.Collections.Generic;
using EngineerNotebook.Data;
using EngineerNotebook.Models;

namespace EngineerNotebook.Services
{
    public class NotesService
    {
        private readonly NotesRepository _repo;

        public NotesService(NotesRepository repo)
        {
            _repo = repo;
        }

        // Categories
        public List<string> GetCategories() => _repo.GetCategories();

        public void AddCategory(string name)
        {
            name = NormalizeCategory(name);
            _repo.AddCategory(name);
        }

        public void DeleteCategory(string name)
        {
            name = NormalizeCategory(name);
            if (name == "Без категории")
                throw new ArgumentException("Категорию 'Без категории' удалять нельзя.");
            _repo.DeleteCategory(name);
        }

        // Notes
        public Note? GetNote(long id) => _repo.GetById(id);

        public List<Note> Search(string query, string categoryFilter)
            => _repo.Search(query, categoryFilter);

        public Note Add(string title, string content, string category, string tags)
        {
            title = NormalizeTitle(title);
            content = NormalizeContent(content);
            category = NormalizeCategory(category);
            tags = NormalizeTags(tags);

            var now = DateTime.Now;
            var note = new Note
            {
                Title = title,
                Content = content,
                Category = category,
                Tags = tags,
                CreatedAt = now,
                UpdatedAt = now
            };

            note.Id = _repo.Insert(note);
            return note;
        }

        public void Update(long id, string title, string content, string category, string tags)
        {
            if (id <= 0) throw new ArgumentException("Некорректный Id заметки.");

            title = NormalizeTitle(title);
            content = NormalizeContent(content);
            category = NormalizeCategory(category);
            tags = NormalizeTags(tags);

            // ✅ Update теперь создаёт НОВУЮ версию (а не “сохраняет старую”)
            _repo.Update(new Note
            {
                Id = id,
                Title = title,
                Content = content,
                Category = category,
                Tags = tags,
                UpdatedAt = DateTime.Now
            });
        }

        public void Delete(long id)
        {
            if (id <= 0) return;
            _repo.Delete(id);
        }

        // Versions
        public List<NoteVersion> GetVersions(long noteId)
        {
            if (noteId <= 0) return new List<NoteVersion>();
            return _repo.GetVersions(noteId);
        }

        public void RestoreVersion(long versionId)
        {
            if (versionId <= 0) return;
            _repo.RestoreFromVersion(versionId);
        }

        private static string NormalizeTitle(string? title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) throw new ArgumentException("Заголовок не может быть пустым.");
            if (title.Length > 120) title = title[..120];
            return title;
        }

        private static string NormalizeContent(string? content)
        {
            content = (content ?? "").Trim();
            if (content.Length > 10000) content = content[..10000];
            return content;
        }

        private static string NormalizeCategory(string? category)
        {
            category = (category ?? "").Trim();
            if (category.Length == 0) category = "Без категории";
            if (category.Length > 60) category = category[..60];
            return category;
        }

        private static string NormalizeTags(string? tags)
        {
            tags = (tags ?? "").Trim();
            if (tags.Length > 200) tags = tags[..200];
            return tags;
        }
    }
}
