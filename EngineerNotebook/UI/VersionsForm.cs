using System;
using System.Linq;
using System.Windows.Forms;
using EngineerNotebook.Models;
using EngineerNotebook.Services;

namespace EngineerNotebook.UI
{
    public class VersionsForm : Form
    {
        private readonly NotesService _service;
        private readonly long _noteId;

        private DataGridView _grid = null!;
        private TextBox _tbTitle = null!;
        private TextBox _tbCategory = null!;
        private TextBox _tbTags = null!;
        private TextBox _tbContent = null!;

        private Button _btnRestore = null!;
        private Button _btnClose = null!;

        private Note? _note;
        private NoteVersion[] _versions = Array.Empty<NoteVersion>();
        private long _selectedVersionId = 0;

        public VersionsForm(NotesService service, long noteId, string noteTitle)
        {
            _service = service;
            _noteId = noteId;

            Text = $"История: {noteTitle}";
            Width = 900;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;

            BuildUi();
            LoadVersions();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(root);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                RowHeadersVisible = false
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Тип",
                DataPropertyName = "Type",
                Width = 110
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Date",
                HeaderText = "Дата",
                DataPropertyName = "Date",
                Width = 170
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Category",
                HeaderText = "Категория",
                DataPropertyName = "Category",
                Width = 220
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Tags",
                HeaderText = "Теги",
                DataPropertyName = "Tags",
                Width = 320
            });

            // скрытая колонка для идентификатора версии
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "VersionId",
                HeaderText = "VersionId",
                DataPropertyName = "VersionId",
                Visible = false
            });

            _grid.CellClick += (_, __) => OnSelectRow();
            root.Controls.Add(_grid, 0, 0);

            var preview = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(preview, 0, 1);

            preview.Controls.Add(new Label { Text = "Заголовок", Dock = DockStyle.Fill }, 0, 0);
            _tbTitle = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            preview.Controls.Add(_tbTitle, 1, 0);

            preview.Controls.Add(new Label { Text = "Категория", Dock = DockStyle.Fill }, 0, 1);
            _tbCategory = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            preview.Controls.Add(_tbCategory, 1, 1);

            preview.Controls.Add(new Label { Text = "Теги", Dock = DockStyle.Fill }, 0, 2);
            _tbTags = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            preview.Controls.Add(_tbTags, 1, 2);

            preview.Controls.Add(new Label { Text = "Текст", Dock = DockStyle.Fill }, 0, 3);
            _tbContent = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            preview.Controls.Add(_tbContent, 1, 3);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            root.Controls.Add(bottom, 0, 2);

            _btnClose = new Button { Text = "Закрыть", Width = 120, Height = 34 };
            _btnRestore = new Button { Text = "Восстановить", Width = 140, Height = 34, Enabled = false };

            _btnClose.Click += (_, __) => Close();
            _btnRestore.Click += (_, __) => RestoreSelected();

            bottom.Controls.Add(_btnClose);
            bottom.Controls.Add(_btnRestore);
        }

        private void LoadVersions()
        {
            _note = _service.GetNote(_noteId);
            if (_note is null)
            {
                MessageBox.Show("Заметка не найдена (возможно, удалена).", "История", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            _versions = _service.GetVersions(_noteId)
                .OrderByDescending(v => v.SavedAt)
                .ToArray();

            // ✅ ВАЖНО: мы НЕ добавляем отдельную строку “Текущая”.
            // Просто для версии, которая совпадает с CurrentVersionId, ставим Type = "Текущая"
            var rows = _versions.Select(v => new
            {
                Type = (v.VersionId == _note.CurrentVersionId) ? "Текущая" : "Версия",
                Date = v.SavedAt.ToString("dd.MM.yyyy HH:mm:ss"),
                Category = string.IsNullOrWhiteSpace(v.Category) ? "Без категории" : v.Category,
                Tags = v.Tags ?? "",
                VersionId = v.VersionId
            }).ToList();

            _grid.DataSource = null;
            _grid.DataSource = rows;

            // Автовыбор текущей версии
            var currentIndex = rows.FindIndex(r => r.VersionId == _note.CurrentVersionId);
            if (currentIndex >= 0 && _grid.Rows.Count > currentIndex)
            {
                _grid.ClearSelection();
                _grid.Rows[currentIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[currentIndex].Cells["Type"];

                var v = _versions.FirstOrDefault(x => x.VersionId == rows[currentIndex].VersionId);
                if (v != null)
                {
                    ShowVersion(v);
                    _selectedVersionId = v.VersionId;
                }
            }

            _btnRestore.Enabled = false;
        }

        private void OnSelectRow()
        {
            if (_note is null) return;
            if (_grid.CurrentRow == null) return;

            var idObj = _grid.CurrentRow.Cells["VersionId"].Value;
            if (idObj == null) return;

            if (!long.TryParse(idObj.ToString(), out var vid)) return;

            var v = _versions.FirstOrDefault(x => x.VersionId == vid);
            if (v == null) return;

            _selectedVersionId = v.VersionId;
            ShowVersion(v);

            // На текущую версию восстановление недоступно
            _btnRestore.Enabled = (v.VersionId != _note.CurrentVersionId);
        }

        private void ShowVersion(NoteVersion v)
        {
            _tbTitle.Text = v.Title;
            _tbCategory.Text = string.IsNullOrWhiteSpace(v.Category) ? "Без категории" : v.Category;
            _tbTags.Text = v.Tags ?? "";
            _tbContent.Text = v.Content;
        }

        private void RestoreSelected()
        {
            if (_note is null) return;
            if (_selectedVersionId <= 0) return;
            if (_selectedVersionId == _note.CurrentVersionId) return;

            var res = MessageBox.Show(
                "Сделать выбранную версию текущей? Новая версия при этом НЕ создаётся.",
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes) return;

            try
            {
                _service.RestoreVersion(_selectedVersionId);

                // Перезагрузить — чтобы сразу обновился столбец "Тип"
                LoadVersions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
