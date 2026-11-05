// 文件职责：处理 OptiTrack 视图的界面事件与初始化钩子。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionShell.Features.OptiTrack.Views;

// 类型职责：OptiTrack 页面代码后置类。
public partial class OptiTrackMainView : UserControl
{
    private Point _lastDragPoint;
    private bool _isDragging;
    private double _currentScale = 1.0;
    private const double MaxZoomScale = 500.0;

    public OptiTrackMainView()
    {
        InitializeComponent();
    }

    private Image? GetCameraImage() => FindName("CameraImage") as Image;

    private static void ClampTranslation(Image image, ScaleTransform scale, TranslateTransform translate)
    {
        // We clamp in the Image's local coordinate system.
        // With RenderTransformOrigin=0.5,0.5 the translation is applied after scaling about the center.
        // When zoomed in, we ensure the scaled content always covers the viewport (no empty background).

        // Viewport size (the visible area for the image). Use ActualWidth/ActualHeight of the Image.
        var viewportW = image.ActualWidth;
        var viewportH = image.ActualHeight;
        if (viewportW <= 0 || viewportH <= 0) return;

        // Content size before scaling: also use ActualWidth/ActualHeight because Image is stretched to fit.
        var contentW = image.ActualWidth;
        var contentH = image.ActualHeight;

        var scaledW = contentW * scale.ScaleX;
        var scaledH = contentH * scale.ScaleY;

        // If scaled content is smaller than viewport, center it (no panning).
        if (scaledW <= viewportW + 0.5)
        {
            translate.X = 0;
        }
        else
        {
            // Max offset so that edges still cover viewport.
            // With origin at center, allowed translate range is +-((scaled - viewport)/2)
            var maxX = (scaledW - viewportW) / 2.0;
            translate.X = Math.Clamp(translate.X, -maxX, maxX);
        }

        if (scaledH <= viewportH + 0.5)
        {
            translate.Y = 0;
        }
        else
        {
            var maxY = (scaledH - viewportH) / 2.0;
            translate.Y = Math.Clamp(translate.Y, -maxY, maxY);
        }
    }

    private void CameraImage_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var image = GetCameraImage();
        if (image?.RenderTransform is not TransformGroup group) return;
        var scale = (ScaleTransform)group.Children[0];
        var translate = (TranslateTransform)group.Children[1];

        const double zoomStep = 1.1;
        var zoom = e.Delta > 0 ? zoomStep : 1 / zoomStep;
        var targetScale = Math.Clamp(scale.ScaleX * zoom, 0.2, MaxZoomScale);
        zoom = targetScale / scale.ScaleX;

        var position = e.GetPosition(image);
        translate.X = position.X - zoom * (position.X - translate.X);
        translate.Y = position.Y - zoom * (position.Y - translate.Y);

        scale.ScaleX = targetScale;
        scale.ScaleY = targetScale;
        _currentScale = targetScale;

        ClampTranslation(image, scale, translate);
    }

    private void CameraImage_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var image = GetCameraImage();
        if (image == null) return;

        _isDragging = true;
        _lastDragPoint = e.GetPosition(this);
        image.CaptureMouse();
    }

    private void CameraImage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var image = GetCameraImage();
        _isDragging = false;
        image?.ReleaseMouseCapture();
    }

    private void CameraImage_OnMouseMove(object sender, MouseEventArgs e)
    {
        var image = GetCameraImage();
        if (!_isDragging || image?.RenderTransform is not TransformGroup group) return;

        var scale = (ScaleTransform)group.Children[0];
        var translate = (TranslateTransform)group.Children[1];

        var current = e.GetPosition(this);
        var delta = current - _lastDragPoint;

        translate.X += delta.X;
        translate.Y += delta.Y;

        ClampTranslation(image, scale, translate);

        _lastDragPoint = current;
    }
}
