using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WDM.Services;
using WDM.ViewModels;

namespace WDM.Views
{
    /// <summary>
    /// 设置中心的代码隐藏。
    ///
    /// 视图专属逻辑（不放进 ViewModel）：
    ///   - 绘制透明度表盘（圆弧、刻度、指针）
    ///   - 鼠标拖动表盘改变透明度
    ///   - 窗口拖动 / 关闭
    ///
    /// 几何参数固定：画布 300×168，圆心 (150,150)，半径 100。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // ---- 表盘几何常量 ----
        private const double Cx = 150;
        private const double Cy = 150;
        private const double Radius = 100;

        private SettingsViewModel Vm => (SettingsViewModel)DataContext;

        private Line? _needle;
        private bool _gaugeBuilt;
        private bool _dragging;

        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel();

            Vm.PropertyChanged += OnVmPropertyChanged;
            Loaded += OnLoaded;
            Closed += (_, _) => Vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_gaugeBuilt)
            {
                BuildGauge();
                _gaugeBuilt = true;
            }
            UpdateNeedle(Vm.GaugeValue);
            ResizeToContent();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.GaugeValue))
                UpdateNeedle(Vm.GaugeValue);
            else if (e.PropertyName == nameof(SettingsViewModel.TechIconMode))
                ResizeToContent(); // 色调滑轨显示 / 隐藏后重新贴合内容高度
        }

        /// <summary>
        /// SizeToContent 在内容动态变化（滑轨显隐）后偶尔不会重新测量，
        /// 这里重置一次，保证窗口高度始终贴合内容，不会被裁掉。
        /// </summary>
        private void ResizeToContent()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!SizeToContent.Equals(SizeToContent.Height)) return;

                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ============================================================================
        // 表盘绘制
        // ============================================================================

        /// <summary>
        /// 在 GaugeCanvas 上绘制：底弧 → 刻度 → 高亮弧 → 指针 → 中心轴。
        /// </summary>
        private void BuildGauge()
        {
            // 底弧（暗色轨道）
            GaugeCanvas.Children.Add(new Path
            {
                Data = CreateArcGeometry(Radius),
                Stroke = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            // 刻度：11 根，均分 180°
            var tickBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            for (int i = 0; i <= 10; i++)
            {
                double angle = Math.PI * (1 - i / 10.0); // 180° → 0°
                bool major = i % 5 == 0;
                double inner = Radius + 8;
                double outer = inner + (major ? 10 : 6);

                GaugeCanvas.Children.Add(new Line
                {
                    X1 = Cx + Math.Cos(angle) * inner,
                    Y1 = Cy - Math.Sin(angle) * inner,
                    X2 = Cx + Math.Cos(angle) * outer,
                    Y2 = Cy - Math.Sin(angle) * outer,
                    Stroke = tickBrush,
                    StrokeThickness = major ? 2 : 1
                });
            }

            // 高亮弧（强调色）
            var accentArc = new Path
            {
                Data = CreateArcGeometry(Radius),
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = (System.Windows.Media.Effects.Effect)FindResource("AccentGlow")
            };
            GaugeCanvas.Children.Add(accentArc);

            // 指针（默认指向正上方，由 RotateTransform 旋转）
            _needle = new Line
            {
                X1 = Cx,
                Y1 = Cy,
                X2 = Cx,
                Y2 = Cy - Radius + 16,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x6B)),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            GaugeCanvas.Children.Add(_needle);

            // 中心轴
            var pivot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x12)),
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2
            };
            Canvas.SetLeft(pivot, Cx - 6);
            Canvas.SetTop(pivot, Cy - 6);
            GaugeCanvas.Children.Add(pivot);
        }

        /// <summary>创建从 180° 到 0° 的上半圆几何。</summary>
        private static PathGeometry CreateArcGeometry(double radius)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(Cx - radius, Cy),
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(new ArcSegment(
                new Point(Cx + radius, Cy),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true));
            return new PathGeometry(new[] { figure });
        }

        /// <summary>根据表盘归一化位置旋转指针：0 → 左端，1 → 右端。</summary>
        private void UpdateNeedle(double normalized)
        {
            if (_needle == null) return;

            double t = Math.Clamp(normalized, 0, 1);
            double angle = -90 + t * 180;
            _needle.RenderTransform = new RotateTransform(angle, Cx, Cy);
        }

        // ============================================================================
        // 表盘交互
        // ============================================================================

        private void Gauge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            GaugeCanvas.CaptureMouse();
            ApplyOpacityFromPosition(e.GetPosition(GaugeCanvas).X);
            e.Handled = true;
        }

        private void Gauge_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            ApplyOpacityFromPosition(e.GetPosition(GaugeCanvas).X);
        }

        private void Gauge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            GaugeCanvas.ReleaseMouseCapture();
        }

        private void Gauge_MouseLeave(object sender, MouseEventArgs e)
        {
            // 鼠标捕获期间离开画布仍继续拖动，这里不处理
        }

        /// <summary>把横向坐标归一化后交给 ViewModel 解释（透明度或色调）。</summary>
        private void ApplyOpacityFromPosition(double x)
        {
            double t = (x - (Cx - Radius)) / (Radius * 2);
            Vm.SetGaugeFromNormalized(Math.Clamp(t, 0, 1));
        }

        // ============================================================================
        // 窗口操作
        // ============================================================================

        /// <summary>点击便签 / 待办 / 图标模式行：切换表盘调节目标。</summary>
        private void PanelRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border row)
                Vm.SelectGaugeTarget(row.Tag?.ToString());
            e.Handled = true;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
