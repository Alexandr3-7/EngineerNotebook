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

        private Note? _currentNote;
        private NoteVersion[] _versions = Array.Empty<NoteVersion>();

        private bool _selectedIsCurrent = true;
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

            // Видимые
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Тип",
                DataPropertyName = "Type",
                Width = 90
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SavedAt",
                HeaderText = "Дата",
                DataPropertyName = "SavedAt",
                Width = 170
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Category",
                HeaderText = "Категория",
                DataPropertyName = "Category",
                Width = 170
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Tags",
                HeaderText = "Теги",
                DataPropertyName = "Tags",
                Width = 300
            });

            // Служебные (скрытые)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "VersionId",
                HeaderText = "VersionId",
                DataPropertyName = "VersionId",
                Visible = false
            });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "IsCurrent",
                HeaderText = "IsCurrent",
                DataPropertyName = "IsCurrent",
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
            _tbContent = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
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
            _currentNote = _service.GetNote(_noteId);
            _versions = _service.GetVersions(_noteId).ToArray();

            if (_currentNote == null)
            {
                MessageBox.Show("Заметка не найдена (возможно, удалена).", "История", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            // ✅ первая строка — текущая версия
            var rows = new[] {
                new
                {
                    Type = "Текущая",
                    SavedAt = _currentNote.UpdatedAt.ToString("dd.MM.yyyy HH:mm:ss"),
                    Category = _currentNote.Category ?? "Без категории",
                    Tags = _currentNote.Tags ?? "",
                    VersionId = 0L,
                    IsCurrent = true
                }
            }
            .Concat(_versions.Select(v => new
            {
                Type = "Версия",
                SavedAt = v.SavedAt.ToString("dd.MM.yyyy HH:mm:ss"),
                Category = v.Category ?? "Без категории",
                Tags = v.Tags ?? "",
                VersionId = v.VersionId,
                IsCurrent = false
            }))
            .ToList();

            _grid.DataSource = null;
            _grid.DataSource = rows;

            // по умолчанию выбираем "текущую"
            if (_grid.Rows.Count > 0)
            {
                _grid.ClearSelection();
                _grid.Rows[0].Selected = true;
                _grid.CurrentCell = _grid.Rows[0].Cells["Type"];
                ShowCurrentPreview();
            }
        }

        private void OnSelectRow()
        {
            if (_grid.CurrentRow == null) return;

            var isCurrentObj = _grid.CurrentRow.Cells["IsCurrent"].Value;
            var versionObj = _grid.CurrentRow.Cells["VersionId"].Value;

            var isCurrent = isCurrentObj is bool b && b;

            _selectedIsCurrent = isCurrent;
            _selectedVersionId = 0;

            if (isCurrent)
            {
                ShowCurrentPreview();
                return;
            }

            if (versionObj == null) return;
            if (!long.TryParse(versionObj.ToString(), out var vid)) return;

            var v = _versions.FirstOrDefault(x => x.VersionId == vid);
            if (v == null) return;

            _selectedVersionId = v.VersionId;

            _tbTitle.Text = v.Title;
            _tbCategory.Text = v.Category;
            _tbTags.Text = v.Tags;
            _tbContent.Text = v.Content;

            _btnRestore.Enabled = true;
        }

        private void ShowCurrentPreview()
        {
            if (_currentNote == null) return;

            _tbTitle.Text = _currentNote.Title;
            _tbCategory.Text = _currentNote.Category ?? "Без категории";
            _tbTags.Text = _currentNote.Tags ?? "";
            _tbContent.Text = _currentNote.Content;

            // на текущую восстанавливать не надо
            _btnRestore.Enabled = false;
        }

        private void RestoreSelected()
        {
            if (_selectedIsCurrent) return;
            if (_selectedVersionId <= 0) return;

            var res = MessageBox.Show(
                "Восстановить выбранную версию? Текущая версия тоже сохранится в истории.",
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes) return;

            try
            {
                _service.RestoreVersion(_selectedVersionId);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
