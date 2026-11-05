// 文件职责：条纹分析实现：从图像估计条纹角度、周期，并给出失败原因。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace VisionShell.Features.OptiTrack.Analysis
{
    // 类型职责：承载一次条纹分析的输出，包含角度、周期、尺寸与错误信息。
    internal sealed record StripeAnalysisResult(bool IsSuccess, double StripeAngle, double NormalAngle, double PeriodPixels, int ImageWidth, int ImageHeight, string Error = "")
    {
        public static StripeAnalysisResult Failure(int width, int height, string error = "分析失败")
            => new(false, double.NaN, double.NaN, double.NaN, width, height, error);

        public static StripeAnalysisResult Create(double stripeAngle, double normalAngle, double period, int width, int height)
            => new(true, stripeAngle, normalAngle, period, width, height);
    }

    // 类型职责：条纹分析静态入口，向上层提供统一 Analyze 调用。
    internal static class StripeAnalysis
    {
        private const int AnalysisCropTargetWidth = 1000;
        private const int AnalysisCropTargetHeight = 1000;

        /// <summary>
        /// Analyze frame data. Supports:
        /// -8-bit Gray (width*height bytes)
        /// -24-bit BGR (width*height*3 bytes)
        /// -10-bit packed into16-bit little-endian (width*height*2 bytes, values0..1023)
        ///
        /// For10-bit data we apply level stretching (min/max normalization) mapping to0..1023.
        /// </summary>
        public static StripeAnalysisResult Analyze(byte[] data, int width, int height, bool useCenterCrop = true, Action<string>? diagLogger = null)
        {
            if (data == null || width <= 0 || height <= 0)
                return StripeAnalysisResult.Failure(width, height, "无效的图像输入");

            try
            {
                diagLogger?.Invoke($"stage=entry inputBytes={data.Length}, size={width}x{height}");
                var gray = BuildGrayMat(data, width, height);
                if (gray == null || gray.Empty())
                {
                    diagLogger?.Invoke("stage=build-gray failed: empty mat");
                    gray?.Dispose();
                    return StripeAnalysisResult.Failure(width, height, "图像为空或转换失败");
                }

                diagLogger?.Invoke($"stage=build-gray ok matType={gray.Type()}, size={gray.Width}x{gray.Height}");

                var analysisInput = useCenterCrop
                    ? BuildCenterCropInput(gray, diagLogger)
                    : gray;
                if (!ReferenceEquals(analysisInput, gray))
                {
                    gray.Dispose();
                }

                if (!useCenterCrop)
                {
                    diagLogger?.Invoke($"stage=center-crop bypass-by-request size={analysisInput.Width}x{analysisInput.Height}");
                }

                using var analyzer = new StripeAnalyzer(analysisInput, debugEnabled: false, diagLogger: diagLogger);
                var res = analyzer.Analyze();
                if (!res.IsSuccess)
                    return StripeAnalysisResult.Failure(width, height, res.Error);

                return StripeAnalysisResult.Create(res.StripeAngle, res.NormalAngle, res.PeriodPixels, res.ImageWidth, res.ImageHeight);
            }
            catch (Exception ex)
            {
                diagLogger?.Invoke($"stage=entry-exception type={ex.GetType().Name}, error={ex.Message}");
                return StripeAnalysisResult.Failure(width, height, ex.Message);
            }
        }

        // Keep native pixel values and only crop center ROI (no resize/compression).
        private static Mat BuildCenterCropInput(Mat gray, Action<string>? diagLogger)
        {
            if (gray.Width <= AnalysisCropTargetWidth && gray.Height <= AnalysisCropTargetHeight)
            {
                diagLogger?.Invoke($"stage=center-crop bypass size={gray.Width}x{gray.Height}, target={AnalysisCropTargetWidth}x{AnalysisCropTargetHeight}");
                return gray;
            }

            int cropWidth = Math.Min(AnalysisCropTargetWidth, gray.Width);
            int cropHeight = Math.Min(AnalysisCropTargetHeight, gray.Height);
            int x = (gray.Width - cropWidth) / 2;
            int y = (gray.Height - cropHeight) / 2;

            using var roi = new Mat(gray, new Rect(x, y, cropWidth, cropHeight));
            var cropped = roi.Clone();
            diagLogger?.Invoke($"stage=center-crop apply rect={x},{y},{cropWidth},{cropHeight}, from={gray.Width}x{gray.Height}");
            return cropped;
        }

        private static Mat BuildGrayMat(byte[] data, int width, int height)
        {
            int expected = width * height;
            if (data.Length < expected)
            {
                return new Mat();
            }

            // ---10-bit packed into16-bit little-endian (common for Mono10/Mono10Packed->16) ---
            // We assume each pixel is stored in2 bytes (UInt16 LE), using lower10 bits (0..1023).
            if (data.Length >= expected * 2 && data.Length < expected * 3)
            {
                return BuildGrayMat10BitFrom16LeWithStretch(data, width, height);
            }

            // --- Gray8 ---
            if (data.Length >= expected && data.Length < expected * 3)
            {
                var gray = new Mat(height, width, MatType.CV_8UC1);
                int len = Math.Min(data.Length, expected);
                Marshal.Copy(data, 0, gray.Data, len);
                return gray;
            }

            // --- BGR24/RGB24 -> Gray8 ---
            if (data.Length >= expected * 3)
            {
                var rgb = new Mat(height, width, MatType.CV_8UC3);
                int len = Math.Min(data.Length, expected * 3);
                Marshal.Copy(data, 0, rgb.Data, len);
                var gray = new Mat();
                Cv2.CvtColor(rgb, gray, ColorConversionCodes.BGR2GRAY);
                rgb.Dispose();
                return gray;
            }

            return new Mat();
        }

        private static Mat BuildGrayMat10BitFrom16LeWithStretch(byte[] data, int width, int height)
        {
            int pixels = width * height;

            // Find min/max in10-bit domain
            ushort min = ushort.MaxValue;
            ushort max =0;

            // Use BitConverter-like reads without allocating
            for (int i =0; i < pixels; i++)
            {
                int o = i *2;
                ushort v = (ushort)(data[o] | (data[o +1] <<8));
                v = (ushort)(v &0x03FF); //10-bit

                if (v < min) min = v;
                if (v > max) max = v;
            }

            // Avoid divide by zero when image is flat
            int span = max - min;

            // Build a16-bit grayscale Mat from managed buffer (no unsafe)
            var buf = new short[pixels];

            if (span <=0)
            {
                // constant value (still in10-bit range), scaled to16-bit
                short val = (short)(min <<6);
                for (int i =0; i < pixels; i++) buf[i] = val;
            }
            else
            {
                for (int i =0; i < pixels; i++)
                {
                    int o = i *2;
                    int v = (data[o] | (data[o +1] <<8)) &0x03FF;
                    int stretched = (v - min) *1023 / span; //0..1023
                    buf[i] = (short)(stretched <<6); //0..65472
                }
            }

            var gray16 = new Mat(height, width, MatType.CV_16UC1);
            Marshal.Copy(buf,0, gray16.Data, pixels);
            return gray16;
        }
    }

    // Adapted from user's console implementation. Accepts a Gray Mat in constructor.
    // 类型职责：OpenCV 分析执行器，完成角度估计、周期测量与置信度校验。
    internal sealed class StripeAnalyzer : IDisposable
    {
        private readonly Mat _gray;
        private readonly Point2f _center;
        private readonly bool _debugEnabled;
        private readonly string _debugDir;
        private readonly Action<string>? _diagLogger;

        // Restored period range from user's program (pixels)
        private const double ExpectedPeriodMinPx =1.62;
        private const double ExpectedPeriodMaxPx =270.27;

        public StripeAnalyzer(Mat gray, bool debugEnabled = false, Action<string>? diagLogger = null)
        {
            _gray = gray ?? throw new ArgumentNullException(nameof(gray));
            _center = new Point2f(_gray.Width /2f, _gray.Height /2f);
            _debugEnabled = debugEnabled;
            _diagLogger = diagLogger;
            _debugDir = _debugEnabled ? Path.Combine(Directory.GetCurrentDirectory(), "stripe_debug") : string.Empty;
            if (_debugEnabled && !Directory.Exists(_debugDir)) Directory.CreateDirectory(_debugDir);
        }

        private void LogDiag(string message)
        {
            _diagLogger?.Invoke(message);
        }

        public StripeAnalysisResult Analyze()
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var angleSw = Stopwatch.StartNew();
                // 1) 先用梯度结构信息估计条纹法线方向，再换算出条纹方向角。
                double normalAngle = EstimateNormalAngleGradient();
                double stripeAngle = (normalAngle +90) %180;
                angleSw.Stop();
                LogDiag($"stage=estimate-angle ok elapsedMs={angleSw.ElapsedMilliseconds}, normal={normalAngle:F4}, stripe={stripeAngle:F4}");

                // 2) 在已知方向的前提下做高精度周期测量，失败时回退到直接测量。
                var hpSw = Stopwatch.StartNew();
                var (refinedPeriod, confidence) = MeasurePeriodHighPrecision(stripeAngle);
                hpSw.Stop();
                LogDiag($"stage=period-highprecision done elapsedMs={hpSw.ElapsedMilliseconds}, period={refinedPeriod:F6}, confidence={confidence:F6}");

                if (double.IsNaN(refinedPeriod) || refinedPeriod <=0)
                {
                    LogDiag("stage=period-highprecision invalid result, fallback=direct");
                    var backupResult = MeasurePeriodDirect();
                    LogDiag($"stage=period-direct fallback period={backupResult.Period:F6}, confidence={backupResult.Confidence:F6}");
                    if (backupResult.Period >0)
                    {
                        refinedPeriod = backupResult.Period;
                        confidence = backupResult.Confidence *0.7;
                    }
                    else
                    {
                        return StripeAnalysisResult.Failure(_gray.Width, _gray.Height, "周期测量失败");
                    }
                }

                if (!IsPeriodValid(refinedPeriod))
                {
                    if (_debugEnabled)
                        Console.WriteLine($"警告: 周期 {refinedPeriod:F2} 超出预期范围");

                    LogDiag($"stage=period-validate out-of-range period={refinedPeriod:F6}, expected={ExpectedPeriodMinPx:F2}-{ExpectedPeriodMaxPx:F2}, fallback=direct");

                    var backupResult = MeasurePeriodDirect();
                    LogDiag($"stage=period-direct validate-fallback period={backupResult.Period:F6}, confidence={backupResult.Confidence:F6}");
                    if (IsPeriodValid(backupResult.Period))
                    {
                        refinedPeriod = backupResult.Period;
                        confidence = backupResult.Confidence *0.6;
                    }
                    else
                    {
                        return StripeAnalysisResult.Failure(_gray.Width, _gray.Height,
                            $"检测到异常周期 {refinedPeriod:F2}px，超出预期范围 {ExpectedPeriodMinPx}-{ExpectedPeriodMaxPx}px");
                    }
                }

                if (confidence <0.3 && _debugEnabled)
                    Console.WriteLine($"警告: 置信度较低 ({confidence:F3})，结果可能不可靠");

                LogDiag($"stage=result success=true stripe={stripeAngle:F4}, normal={normalAngle:F4}, period={refinedPeriod:F6}, confidence={confidence:F6}, totalMs={totalSw.ElapsedMilliseconds}");

                // Return result (map to existing record fields)
                return StripeAnalysisResult.Create(stripeAngle, normalAngle, refinedPeriod, _gray.Width, _gray.Height);
            }
            catch (Exception ex)
            {
                LogDiag($"stage=result success=false type={ex.GetType().Name}, error={ex.Message}, totalMs={totalSw.ElapsedMilliseconds}");
                return StripeAnalysisResult.Failure(_gray.Width, _gray.Height, $"周期测量异常: {ex.Message}");
            }
        }

        private bool IsPeriodValid(double period)
        {
            if (period < ExpectedPeriodMinPx || period > ExpectedPeriodMaxPx)
                return false;

            double maxReasonablePeriod = Math.Sqrt(_gray.Width * _gray.Width + _gray.Height * _gray.Height) *0.3;
            if (period > maxReasonablePeriod) return false;
            return true;
        }

        private double EstimateNormalAngleGradient()
        {
            using var dx = new Mat();
            using var dy = new Mat();

            Cv2.Sobel(_gray, dx, MatType.CV_32F,1,0,3);
            Cv2.Sobel(_gray, dy, MatType.CV_32F,0,1,3);

            double sxx = dx.Dot(dx);
            double syy = dy.Dot(dy);
            double sxy = dx.Dot(dy);

            double angleRad =0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double angleDeg = angleRad *180.0 / Math.PI;
            if (angleDeg <0) angleDeg +=180;
            return angleDeg;
        }

        private (double Period, double Confidence) MeasurePeriodHighPrecision(double stripeAngle)
        {
            string stage = "init";
            Mat? cropped = null;
            try
            {
                // Align to the console algorithm: keep signed rotation and normalize to [-90, 90].
                stage = "rotate-angle";
                double rotationAngle = stripeAngle -90.0;
                if (rotationAngle >90.0) rotationAngle -=180.0;
                if (rotationAngle < -90.0) rotationAngle +=180.0;

                stage = "rotate-image";
                using var rotated = RotateImageForAnalysis(_gray, rotationAngle);
                LogDiag($"stage=period-highprecision rotated size={rotated.Width}x{rotated.Height}, type={rotated.Type()}, rotation={rotationAngle:F4}");

                stage = "crop-valid-region";
                cropped = CropValidRegionConservative(rotated);
                LogDiag($"stage=period-highprecision cropped size={cropped.Width}x{cropped.Height}, type={cropped.Type()}");

                stage = "projection-primary";
                double[] signal = ComputeColumnProjection(cropped);
                if (signal.Length <32)
                {
                    stage = "projection-fallback-center-crop";
                    cropped.Dispose();
                    int size = Math.Min(rotated.Width, rotated.Height) /2;
                    int x = (rotated.Width - size) /2;
                    int y = (rotated.Height - size) /2;
                    cropped = new Mat(rotated, new Rect(x, y, size, size));
                    signal = ComputeColumnProjection(cropped);
                    LogDiag($"stage=period-highprecision projection-fallback signalLength={signal.Length}, cropSize={size}");
                }

                if (signal.Length <16)
                {
                    LogDiag($"stage=period-highprecision signal-too-short length={signal.Length}");
                    return (0,0);
                }

                stage = "fft-estimate";
                double fftPeriod = EstimatePeriod1DFFT_Original(signal, out double fftConfidence);
                LogDiag($"stage=period-highprecision fft period={fftPeriod:F6}, confidence={fftConfidence:F6}, signalLength={signal.Length}");
                if (fftPeriod <=0 || double.IsNaN(fftPeriod))
                {
                    return (0,0);
                }

                stage = "sine-fit";
                double refinedPeriod;
                double fitRmse;
                try
                {
                    var fitResult = FitSineWave(signal, fftPeriod);
                    refinedPeriod = fitResult.Period;
                    fitRmse = fitResult.Rmse;
                    if (fitRmse >50 || double.IsNaN(refinedPeriod))
                    {
                        refinedPeriod = fftPeriod;
                        fitRmse = double.MaxValue;
                    }
                }
                catch (Exception fitEx)
                {
                    LogDiag($"stage=period-highprecision fit-exception type={fitEx.GetType().Name}, error={fitEx.Message}");
                    refinedPeriod = fftPeriod;
                    fitRmse = double.MaxValue;
                }

                if (Math.Abs(refinedPeriod - fftPeriod) / fftPeriod >0.3)
                {
                    refinedPeriod = fftPeriod;
                    fitRmse =100.0;
                }

                double finalConfidence = Math.Max(0, Math.Min(1, fftConfidence * (1.0 / (1.0 + fitRmse /50))));

                if (refinedPeriod <5.0 && finalConfidence <0.5)
                {
                    stage = "direct-backup-compare";
                    var backupResult = MeasurePeriodDirect();
                    if (backupResult.Confidence > finalConfidence *1.2 && IsPeriodValid(backupResult.Period))
                    {
                        refinedPeriod = backupResult.Period;
                        finalConfidence = backupResult.Confidence *0.9;
                        LogDiag($"stage=period-highprecision backup-adopted period={refinedPeriod:F6}, confidence={finalConfidence:F6}");
                    }
                }

                return (refinedPeriod, finalConfidence);
            }
            catch (Exception ex)
            {
                LogDiag($"stage=period-highprecision exceptionAt={stage}, type={ex.GetType().Name}, error={ex.Message}");
                throw new InvalidOperationException($"高精度测量步骤[{stage}]异常: {ex.Message}", ex);
            }
            finally
            {
                cropped?.Dispose();
            }
        }

        private Mat RotateImageForAnalysis(Mat src, double angle)
        {
            if (Math.Abs(angle %180) <0.1)
                return src.Clone();

            Point2f center = new Point2f(src.Width /2f, src.Height /2f);
            using var rotMat = Cv2.GetRotationMatrix2D(center, angle,1.0);

            double rad = Math.Abs(angle * Math.PI /180.0);
            double cosA = Math.Abs(Math.Cos(rad));
            double sinA = Math.Abs(Math.Sin(rad));

            int newWidth = (int)(src.Width * cosA + src.Height * sinA);
            int newHeight = (int)(src.Width * sinA + src.Height * cosA);

            rotMat.Set(0,2, rotMat.At<double>(0,2) + (newWidth - src.Width) /2.0);
            rotMat.Set(1,2, rotMat.At<double>(1,2) + (newHeight - src.Height) /2.0);

            var result = new Mat();
            InterpolationFlags interpolation = Math.Abs(angle) >30 ? InterpolationFlags.Lanczos4 : InterpolationFlags.Cubic;

            Cv2.WarpAffine(src, result, rotMat, new Size(newWidth, newHeight),
                interpolation, BorderTypes.Replicate, Scalar.All(0));

            return result;
        }

        private Mat CropValidRegionConservative(Mat rotated)
        {
            using var binary = new Mat();
            Cv2.Threshold(rotated, binary,1,255, ThresholdTypes.Binary);

            var points = rotated.FindNonZero();
            if (points.Empty())
                return rotated.Clone();

            Rect boundingRect = Cv2.BoundingRect(points);

            int margin =10;
            boundingRect.X = Math.Max(0, boundingRect.X - margin);
            boundingRect.Y = Math.Max(0, boundingRect.Y - margin);
            boundingRect.Width = Math.Min(rotated.Width - boundingRect.X, boundingRect.Width +2 * margin);
            boundingRect.Height = Math.Min(rotated.Height - boundingRect.Y, boundingRect.Height +2 * margin);

            if (boundingRect.Width <=50 || boundingRect.Height <=50)
                return rotated.Clone();

            return new Mat(rotated, boundingRect);
        }

        private double EstimatePeriod1DFFT_Original(double[] signal, out double confidence)
        {
            int n = signal.Length;
            double[] detrended = DetrendSignal(signal);
            double[] windowed = ApplyWindow(detrended);

            int m = GetNextPowerOf2(n);
            using var complexMat = new Mat(m,1, MatType.CV_32FC2);

            for (int i =0; i < m; i++)
            {
                float val = (i < n) ? (float)windowed[i] :0f;
                complexMat.Set(i,0, new Vec2f(val,0));
            }

            Cv2.Dft(complexMat, complexMat, DftFlags.ComplexOutput);

            int halfM = m /2;
            double[] magnitude = new double[halfM];

            for (int k =0; k < halfM; k++)
            {
                Vec2f c = complexMat.At<Vec2f>(k,0);
                magnitude[k] = Math.Sqrt(c.Item0 * c.Item0 + c.Item1 * c.Item1);
            }

            int minK = Math.Max(1, (int)Math.Ceiling(m / ExpectedPeriodMaxPx));
            int maxK = Math.Min(halfM -1, (int)Math.Floor(m / ExpectedPeriodMinPx));

            if (maxK - minK <10)
            {
                minK = Math.Max(1, minK -5);
                maxK = Math.Min(halfM -1, maxK +5);
            }

            double maxMag =0;
            int peakIdx = -1;
            for (int k = minK; k <= maxK; k++)
            {
                if (magnitude[k] > maxMag)
                {
                    maxMag = magnitude[k];
                    peakIdx = k;
                }
            }

            if (peakIdx == -1)
            {
                for (int k =1; k < halfM /2; k++)
                {
                    if (magnitude[k] > maxMag)
                    {
                        maxMag = magnitude[k];
                        peakIdx = k;
                    }
                }
            }

            if (peakIdx == -1 || maxMag <1e-9)
            {
                confidence =0;
                return 0;
            }

            double kPeak = peakIdx;
            if (peakIdx >0 && peakIdx < halfM -1)
            {
                double y0 = magnitude[peakIdx];
                double ym1 = magnitude[peakIdx -1];
                double yp1 = magnitude[peakIdx +1];

                double denom = ym1 -2 * y0 + yp1;
                if (Math.Abs(denom) >1e-9)
                {
                    double p =0.5 * (ym1 - yp1) / denom;
                    kPeak = peakIdx + p;
                }
            }

            double measuredPeriod = m / kPeak;

            double noiseSum =0;
            int noiseCount =0;
            int excludeStart = Math.Max(0, peakIdx -2);
            int excludeEnd = Math.Min(halfM -1, peakIdx +2);

            for (int k =1; k < halfM; k++)
            {
                if (k < excludeStart || k > excludeEnd)
                {
                    noiseSum += magnitude[k];
                    noiseCount++;
                }
            }

            double noiseLevel = noiseCount >0 ? noiseSum / noiseCount :1;
            confidence = (noiseLevel >1e-9) ? maxMag / noiseLevel :0;
            confidence = Math.Min(1.0, confidence /15.0);

            return measuredPeriod;
        }

        private (double Period, double Confidence) MeasurePeriodDirect()
        {
            double[] hSignal = ComputeRowProjection(_gray);
            var hResult = EstimatePeriod1DFFT_Original(hSignal, out double hConfidence);

            double[] vSignal = ComputeColumnProjection(_gray);
            var vResult = EstimatePeriod1DFFT_Original(vSignal, out double vConfidence);

            if (hConfidence > vConfidence && IsPeriodValid(hResult))
                return (hResult, hConfidence);
            else if (IsPeriodValid(vResult))
                return (vResult, vConfidence);

            if (hConfidence > vConfidence)
                return (hResult, hConfidence *0.5);
            else
                return (vResult, vConfidence *0.5);
        }

        private double[] ComputeRowProjection(Mat img)
        {
            int h = img.Height;
            int w = img.Width;
            double[] proj = new double[h];
            for (int y =0; y < h; y++)
            {
                using var row = img.SubMat(y, y +1,0, w);
                Scalar s = Cv2.Sum(row);
                proj[y] = s.Val0 / w;
            }
            return proj;
        }

        private double[] ComputeColumnProjection(Mat img)
        {
            int w = img.Width;
            int h = img.Height;
            double[] proj = new double[w];
            for (int x =0; x < w; x++)
            {
                using var col = img.SubMat(0, h, x, x +1);
                Scalar s = Cv2.Sum(col);
                proj[x] = s.Val0 / h;
            }
            return proj;
        }

        private (double Period, double Rmse) FitSineWave(double[] y, double tGuess)
        {
            int n = y.Length;
            double[] x = new double[n];
            for (int i =0; i < n; i++) x[i] = i;

            double bestT = tGuess;
            double bestResidue = double.MaxValue;

            double tMin = tGuess *0.8;
            double tMax = tGuess *1.2;
            int steps =50;

            for (int i =0; i <= steps; i++)
            {
                double t = tMin + (tMax - tMin) * i / steps;
                if (t <1) continue;

                double w =2 * Math.PI / t;

                double sumSin =0, sumCos =0, sumSin2 =0, sumCos2 =0, sumSinCos =0, sumY =0;
                double sumYSin =0, sumYCos =0;

                for (int j =0; j < n; j++)
                {
                    double sw = Math.Sin(w * x[j]);
                    double cw = Math.Cos(w * x[j]);
                    double yv = y[j];

                    sumSin += sw;
                    sumCos += cw;
                    sumSin2 += sw * sw;
                    sumCos2 += cw * cw;
                    sumSinCos += sw * cw;
                    sumY += yv;
                    sumYSin += yv * sw;
                    sumYCos += yv * cw;
                }

                var matA = new double[,] {
                    { sumSin2, sumSinCos, sumSin },
                    { sumSinCos, sumCos2, sumCos },
                    { sumSin, sumCos, n }
                };
                var vecB = new double[] { sumYSin, sumYCos, sumY };

                var coeffs = SolveLinear3x3(matA, vecB);
                if (coeffs == null) continue;

                double A = coeffs[0];
                double B = coeffs[1];
                double C = coeffs[2];

                double residue =0;
                for (int j =0; j < n; j++)
                {
                    double pred = A * Math.Sin(w * x[j]) + B * Math.Cos(w * x[j]) + C;
                    double err = y[j] - pred;
                    residue += err * err;
                }

                if (residue < bestResidue)
                {
                    bestResidue = residue;
                    bestT = t;
                }
            }

            return (bestT, Math.Sqrt(bestResidue / n));
        }

        private double[] DetrendSignal(double[] signal)
        {
            int n = signal.Length;
            double[] detrended = new double[n];

            double sumX =0, sumY =0, sumXY =0, sumX2 =0;

            for (int i =0; i < n; i++)
            {
                sumX += i;
                sumY += signal[i];
                sumXY += i * signal[i];
                sumX2 += i * i;
            }

            double det = n * sumX2 - sumX * sumX;
            double slope =0, intercept =0;

            if (Math.Abs(det) >1e-9)
            {
                slope = (n * sumXY - sumX * sumY) / det;
                intercept = (sumY - slope * sumX) / n;
            }
            else
            {
                intercept = sumY / n;
            }

            for (int i =0; i < n; i++)
            {
                detrended[i] = signal[i] - (slope * i + intercept);
            }

            return detrended;
        }

        private double[] ApplyWindow(double[] signal)
        {
            int n = signal.Length;
            double[] windowed = new double[n];

            for (int i =0; i < n; i++)
            {
                double window =0.5 * (1 - Math.Cos(2 * Math.PI * i / (n -1)));
                windowed[i] = signal[i] * window;
            }

            return windowed;
        }

        private double[]? SolveLinear3x3(double[,] A, double[] B)
        {
            double Det(double[,] m) =>
                m[0,0] * (m[1,1] * m[2,2] - m[1,2] * m[2,1]) -
                m[0,1] * (m[1,0] * m[2,2] - m[1,2] * m[2,0]) +
                m[0,2] * (m[1,0] * m[2,1] - m[1,1] * m[2,0]);

            double detA = Det(A);
            if (Math.Abs(detA) <1e-9) return null;

            var A0 = (double[,])A.Clone(); A0[0,0] = B[0]; A0[1,0] = B[1]; A0[2,0] = B[2];
            var A1 = (double[,])A.Clone(); A1[0,1] = B[0]; A1[1,1] = B[1]; A1[2,1] = B[2];
            var A2 = (double[,])A.Clone(); A2[0,2] = B[0]; A2[1,2] = B[1]; A2[2,2] = B[2];

            return new double[] { Det(A0) / detA, Det(A1) / detA, Det(A2) / detA };
        }

        private int GetNextPowerOf2(int n)
        {
            int p =1;
            while (p < n) p <<=1;
            return p;
        }

        public void Dispose()
        {
            _gray.Dispose();
        }
    }
}
