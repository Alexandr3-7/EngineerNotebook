using System;
using System.Linq;
using System.Windows.Forms;
using EngineerNotebook.Services;

namespace EngineerNotebook.UI
{
    public class CategoriesForm : Form
    {
        private readonly NotesService _service;

        private ListBox _list = null!;
        private TextBox _tbNew = null!;
        private Button _btnAdd = null!;
        private Button _btnDelete = null!;
        private Button _btnClose = null!;

        public CategoriesForm(NotesService service)
        {
            _service = service;

            Text = "Категории";
            Width = 420;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            BuildUi();
            Reload();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // new
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); // buttons
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); // close
            Controls.Add(root);

            _list = new ListBox { Dock = DockStyle.Fill };
            root.Controls.Add(_list, 0, 0);

            _tbNew = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Новая категория (например: Объект 12)"
            };
            _tbNew.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) AddCategory(); };
            root.Controls.Add(_tbNew, 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            root.Controls.Add(actions, 0, 2);

            _btnAdd = new Button { Text = "Добавить", Width = 120, Height = 34 };
            _btnDelete = new Button { Text = "Удалить", Width = 120, Height = 34 };
            actions.Controls.Add(_btnAdd);
            actions.Controls.Add(_btnDelete);

            _btnAdd.Click += (_, __) => AddCategory();
            _btnDelete.Click += (_, __) => DeleteSelected();

            _btnClose = new Button { Text = "Закрыть", Dock = DockStyle.Fill, Height = 34 };
            _btnClose.Click += (_, __) => Close();
            root.Controls.Add(_btnClose, 0, 3);
        }

        private void Reload()
        {
            var items = _service.GetCategories().ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var c in items) _list.Items.Add(c);
            _list.EndUpdate();
        }

        private void AddCategory()
        {
            var name = (_tbNew.Text ?? "").Trim();
            if (name.Length == 0) return;

            try
            {
                _service.AddCategory(name);
                _tbNew.Text = "";
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItem == null) return;

            var name = _list.SelectedItem.ToString() ?? "";
            if (name == "Без категории")
            {
                MessageBox.Show("Категорию 'Без категории' удалять нельзя.",
                    "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var res = MessageBox.Show(
                $"Удалить категорию '{name}'?\nЗаметки будут переведены в 'Без категории'.",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (res != DialogResult.Yes) return;

            try
            {
                _service.DeleteCategory(name);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
