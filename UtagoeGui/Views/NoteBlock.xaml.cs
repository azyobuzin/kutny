using System.Windows;
using System.Windows.Controls;

namespace UtagoeGui.Views
{
    /// <summary>
    /// NoteBlock.xaml の相互作用ロジック
    /// </summary>
    public partial class NoteBlock : UserControl
    {
        public NoteBlock()
        {
            InitializeComponent();
        }

        public string Text
        {
            get => (string)this.GetValue(TextProperty);
            set => this.SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                "Text", typeof(string), typeof(NoteBlock),
                new PropertyMetadata("", (d, e) => ((NoteBlock)d).textBlock.Text = (string)e.NewValue)
            );
    }
}
