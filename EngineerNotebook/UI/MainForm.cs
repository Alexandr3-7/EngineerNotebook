using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using EngineerNotebook.Data;
using EngineerNotebook.Models;
using EngineerNotebook.Services;

namespace EngineerNotebook.UI
{
    public partial class MainForm : Form
    {
        private readonly NotesService _service;

        private DataGridView _grid = null!;
        private TextBox _tbSearch = null!;
        private ComboBox _cbFilterCategory = null!;
        private Label _lblInfo = null!;

        private TextBox _tbTitle = null!;
        private TextBox _tbContent = null!;
        private ComboBox _cbCategory = null!;
        private TextBox _tbTags = null!;

        private Button _btnAdd = null!;
        private Button _btnUpdate = null!;
        private Button _btnDelete = null!;
        private Button _btnClear = null!;
        private Button _btnHistory = null!;

        private List<Note> _current = new();
        private long _selectedId = 0;

        public MainForm()
        {
            InitializeComponent();

            Text = "Записная книжка инженера-проектировщика";
            Width = 1200;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;

            _service = new NotesService(new NotesRepository());

            BuildUi();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            ReloadCategories();
            RefreshList();
        }

        private static void SetNiceMargins(Control c, int left = 6, int top = 8, int right = 6, int bottom = 8)
        {
            c.Margin = new Padding(left, top, right, bottom);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // ---- TOP ----
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                Padding = new Padding(0, 4, 0, 0)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            root.Controls.Add(top, 0, 0);
            root.SetColumnSpan(top, 2);

            var lblSearch = new Label { Text = "Поиск:", AutoSize = true, Anchor = AnchorStyles.Left };
            SetNiceMargins(lblSearch);

            _tbSearch = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            SetNiceMargins(_tbSearch, top: 7, bottom: 7);

            var lblCat = new Label { Text = "Категория:", AutoSize = true, Anchor = AnchorStyles.Left };
            SetNiceMargins(lblCat);

            _cbFilterCategory = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            SetNiceMargins(_cbFilterCategory, top: 7, bottom: 7);
            _cbFilterCategory.SelectedIndexChanged += (_, __) => RefreshList();

            var btnSearch = new Button { Text = "Найти", Width = 80, Height = 28, Anchor = AnchorStyles.Left };
            SetNiceMargins(btnSearch, top: 6, bottom: 6);

            var btnReset = new Button { Text = "Сброс", Width = 80, Height = 28, Anchor = AnchorStyles.Left };
            SetNiceMargins(btnReset, top: 6, bottom: 6);

            var btnCategories = new Button { Text = "Категории…", Width = 110, Height = 28, Anchor = AnchorStyles.Left };
            SetNiceMargins(btnCategories, top: 6, bottom: 6);

            _lblInfo = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left };
            SetNiceMargins(_lblInfo);

            btnSearch.Click += (_, __) => RefreshList();
            btnReset.Click += (_, __) =>
            {
                _tbSearch.Text = "";
                if (_cbFilterCategory.Items.Count > 0) _cbFilterCategory.SelectedIndex = 0;
                RefreshList();
            };
            _tbSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshList(); };

            btnCategories.Click += (_, __) =>
            {
                using var f = new CategoriesForm(_service);
                f.ShowDialog(this);
                ReloadCategories();
                RefreshList();
            };

            top.Controls.Add(lblSearch, 0, 0);
            top.Controls.Add(_tbSearch, 1, 0);
            top.Controls.Add(lblCat, 2, 0);
            top.Controls.Add(_cbFilterCategory, 3, 0);
            top.Controls.Add(btnSearch, 4, 0);
            top.Controls.Add(btnReset, 5, 0);
            top.Controls.Add(btnCategories, 6, 0);
            top.Controls.Add(_lblInfo, 7, 0);

            // ---- GRID ----
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

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Заголовок", DataPropertyName = "Title", Width = 230 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Категория", DataPropertyName = "Category", Width = 140 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tags", HeaderText = "Теги", DataPropertyName = "Tags", Width = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UpdatedAt", HeaderText = "Обновлено", DataPropertyName = "UpdatedAt", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", DataPropertyName = "Id", Width = 60 });

            _grid.CellClick += (_, __) => OnSelectRow();
            root.Controls.Add(_grid, 0, 1);

            // ---- RIGHT ----
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 10
            };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));

            root.Controls.Add(right, 1, 1);

            right.Controls.Add(new Label { Text = "Заголовок", Dock = DockStyle.Fill }, 0, 0);
            _tbTitle = new TextBox { Dock = DockStyle.Fill };
            right.Controls.Add(_tbTitle, 0, 1);

            right.Controls.Add(new Label { Text = "Категория", Dock = DockStyle.Fill }, 0, 2);

            var catPanel = new Panel { Dock = DockStyle.Fill };
            _cbCategory = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var btnManageCats = new Button { Text = "Категории…", Dock = DockStyle.Right, Width = 120 };
            btnManageCats.Click += (_, __) =>
            {
                using var f = new CategoriesForm(_service);
                f.ShowDialog(this);
                ReloadCategories();
            };

            catPanel.Controls.Add(_cbCategory);
            catPanel.Controls.Add(btnManageCats);
            right.Controls.Add(catPanel, 0, 3);

            right.Controls.Add(new Label { Text = "Теги (через запятую)", Dock = DockStyle.Fill }, 0, 4);
            _tbTags = new TextBox { Dock = DockStyle.Fill };
            right.Controls.Add(_tbTags, 0, 5);

            right.Controls.Add(new Label { Text = "Текст заметки", Dock = DockStyle.Fill }, 0, 6);
            _tbContent = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            right.Controls.Add(_tbContent, 0, 7);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

            _btnAdd = new Button { Text = "Добавить", Width = 110, Height = 35 };
            _btnUpdate = new Button { Text = "Сохранить", Width = 110, Height = 35, Enabled = false };
            _btnDelete = new Button { Text = "Удалить", Width = 110, Height = 35, Enabled = false };
            _btnHistory = new Button { Text = "История", Width = 110, Height = 35, Enabled = false };
            _btnClear = new Button { Text = "Очистить", Width = 110, Height = 35 };

            _btnAdd.Click += (_, __) => AddNote();
            _btnUpdate.Click += (_, __) => UpdateNote();
            _btnDelete.Click += (_, __) => DeleteNote();
            _btnHistory.Click += (_, __) => OpenHistory();
            _btnClear.Click += (_, __) => ClearEditor();

            btnPanel.Controls.AddRange(new Control[] { _btnAdd, _btnUpdate, _btnDelete, _btnHistory, _btnClear });
            right.Controls.Add(btnPanel, 0, 8);
        }

        private void ReloadCategories()
        {
            var cats = _service.GetCategories();

            _cbFilterCategory.Items.Clear();
            _cbFilterCategory.Items.Add("Все");
            foreach (var c in cats) _cbFilterCategory.Items.Add(c);
            _cbFilterCategory.SelectedIndex = 0;

            var current = _cbCategory.SelectedItem?.ToString() ?? "Без категории";
            _cbCategory.Items.Clear();
            foreach (var c in cats) _cbCategory.Items.Add(c);

            _cbCategory.SelectedItem = _cbCategory.Items.Contains(current) ? current : "Без категории";
            if (_cbCategory.SelectedItem == null && _cbCategory.Items.Count > 0)
                _cbCategory.SelectedIndex = 0;
        }

        private void RefreshList()
        {
            var query = _tbSearch.Text;
            var cat = _cbFilterCategory.SelectedItem?.ToString() ?? "Все";

            _current = _service.Search(query, cat);
            BindGrid(_current);

            _lblInfo.Text = $"Найдено: {_current.Count}";
        }

        private void BindGrid(List<Note> notes)
        {
            _grid.DataSource = null;
            _grid.DataSource = notes.Select(n => new
            {
                n.Id,
                n.Title,
                n.Category,
                Tags = n.Tags ?? "",
                UpdatedAt = n.UpdatedAt.ToString("dd.MM.yyyy HH:mm")
            }).ToList();

            _selectedId = 0;
            _btnUpdate.Enabled = false;
            _btnDelete.Enabled = false;
            _btnHistory.Enabled = false;
        }

        private void OnSelectRow()
        {
            if (_grid.CurrentRow == null) return;

            var idObj = _grid.CurrentRow.Cells["Id"].Value;
            if (idObj == null) return;
            if (!long.TryParse(idObj.ToString(), out var id)) return;

            var note = _current.FirstOrDefault(n => n.Id == id);
            if (note == null) return;

            _selectedId = note.Id;
            _tbTitle.Text = note.Title;
            _tbContent.Text = note.Content;
            _tbTags.Text = note.Tags ?? "";

            var cat = string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category;
            _cbCategory.SelectedItem = _cbCategory.Items.Contains(cat) ? cat : "Без категории";

            _btnUpdate.Enabled = true;
            _btnDelete.Enabled = true;
            _btnHistory.Enabled = true;
        }

        private void OpenHistory()
        {
            if (_selectedId <= 0) return;

            var title = (_tbTitle.Text ?? "").Trim();
            if (title.Length == 0) title = $"Id {_selectedId}";

            using var f = new VersionsForm(_service, _selectedId, title);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                ReloadCategories();
                RefreshList();
            }
        }

        private void AddNote()
        {
            try
            {
                var cat = _cbCategory.SelectedItem?.ToString() ?? "Без категории";
                _service.Add(_tbTitle.Text, _tbContent.Text, cat, _tbTags.Text);

                ClearEditor();
                ReloadCategories();
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateNote()
        {
            try
            {
                if (_selectedId <= 0) return;

                var cat = _cbCategory.SelectedItem?.ToString() ?? "Без категории";
                _service.Update(_selectedId, _tbTitle.Text, _tbContent.Text, cat, _tbTags.Text);

                ReloadCategories();
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteNote()
        {
            try
            {
                if (_selectedId <= 0) return;

                var res = MessageBox.Show("Удалить выбранную заметку?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (res != DialogResult.Yes) return;

                _service.Delete(_selectedId);
                ClearEditor();
                ReloadCategories();
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ClearEditor()
        {
            _selectedId = 0;
            _tbTitle.Text = "";
            _tbContent.Text = "";
            _tbTags.Text = "";

            if (_cbCategory.Items.Contains("Без категории"))
                _cbCategory.SelectedItem = "Без категории";

            _btnUpdate.Enabled = false;
            _btnDelete.Enabled = false;
            _btnHistory.Enabled = false;
        }
    }
}
