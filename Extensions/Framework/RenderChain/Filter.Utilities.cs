﻿// This file is a part of MPDN Extensions.
// https://github.com/zachsaw/MPDN_Extensions
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mpdn.RenderScript;
using SharpDX;
using IBaseFilter = Mpdn.Extensions.Framework.RenderChain.IFilter<Mpdn.IBaseTexture>;

namespace Mpdn.Extensions.Framework.RenderChain
{
    public abstract class BasicFilter : Filter
    {
        protected BasicFilter(IFilter inputFilter)
            : base(inputFilter)
        {
        }

        protected abstract void Render(ITexture2D input);

        protected override void Render(IList<IBaseTexture> inputs)
        {
            var texture = inputs.OfType<ITexture2D>().SingleOrDefault();
            if (texture == null)
                return;

            Render(texture);
        }

        public override TextureSize OutputSize
        {
            get { return InputFilters[0].OutputSize; }
        }

        public override TextureFormat OutputFormat
        {
            get { return InputFilters[0].OutputFormat; }
        }

    }

    public sealed class RgbFilter : BasicFilter
    {
        public readonly YuvColorimetric Colorimetric;
        public readonly bool OutputLimitedRange;
        public readonly bool OutputLimitChroma;

        public RgbFilter(IFilter inputFilter, bool limitedRange)
            : this(inputFilter, null, limitedRange)
        {
        }

        public RgbFilter(IFilter inputFilter, YuvColorimetric? colorimetric = null, bool? limitedRange = null, bool? limitChroma = null)
            : base(inputFilter)
        {
            Colorimetric = colorimetric ?? Renderer.Colorimetric;
            OutputLimitedRange = limitedRange ?? Renderer.OutputLimitedRange;
            OutputLimitChroma = limitChroma ?? Renderer.LimitChroma;
        }

        protected override IFilter<ITexture2D> Optimize()
        {
            var input = InputFilters[0] as YuvFilter;
            if (input != null && input.Colorimetric == Colorimetric && input.OutputLimitedRange == OutputLimitedRange)
                return (IFilter<ITexture2D>) input.InputFilters[0];

            return this;
        }

        protected override void Render(ITexture2D input)
        {
            Renderer.ConvertToRgb(OutputTarget, input, Colorimetric, OutputLimitedRange, OutputLimitChroma);
        }
    }

    public sealed class YuvFilter : BasicFilter
    {
        public readonly YuvColorimetric Colorimetric;
        public readonly bool OutputLimitedRange;

        public YuvFilter(IFilter inputFilter, bool limitedRange)
            : this(inputFilter, null, limitedRange)
        {
        }

        public YuvFilter(IFilter inputFilter, YuvColorimetric? colorimetric = null, bool? limitedRange = null)
            : base(inputFilter)
        {
            Colorimetric = colorimetric ?? Renderer.Colorimetric;
            OutputLimitedRange = limitedRange ?? Renderer.OutputLimitedRange;
        }

        protected override IFilter<ITexture2D> Optimize()
        {
            var input = InputFilters[0] as RgbFilter;
            if (input != null && input.Colorimetric == Colorimetric && input.OutputLimitedRange == OutputLimitedRange)
                return (IFilter<ITexture2D>) input.InputFilters[0];

            var sourceFilter = InputFilters[0] as SourceFilter;
            if (sourceFilter != null)
                return sourceFilter.GetYuv();

            return this;
        }

        protected override void Render(ITexture2D input)
        {
            Renderer.ConvertToYuv(OutputTarget, input, Colorimetric, OutputLimitedRange);
        }
    }

    public sealed class ChromaSourceFilter : ShaderFilter
    {
        public ChromaSourceFilter()
            : base(GetShader(), new USourceFilter(), new VSourceFilter())
        {
        }

        private static IShader GetShader()
        {
            var asmPath = typeof (IRenderScript).Assembly.Location;
            var shaderDataFilePath =
                Path.Combine(PathHelper.GetDirectoryName(asmPath),
                    "Extensions", "RenderScripts", "Common");
            return ShaderCache.CompileShader(Path.Combine(shaderDataFilePath, "MergeChromaYZFromSource.hlsl"));
        }
    }

    public class ResizeFilter : Filter, IResizeableFilter
    {
        private TextureSize m_OutputSize;
        private readonly TextureChannels m_Channels;
        private readonly Vector2 m_Offset;

        private readonly IScaler m_Downscaler;
        private readonly IScaler m_Upscaler;
        private IScaler m_Convolver;
        private bool m_Tagged;

        public ResizeFilter(IFilter<ITexture2D> inputFilter, 
            IScaler upscaler = null, IScaler downscaler = null, IScaler convolver = null)
            : this(inputFilter, inputFilter.OutputSize, TextureChannels.All, Vector2.Zero, upscaler, downscaler, convolver)
        { }

        public ResizeFilter(IFilter<ITexture2D> inputFilter, TextureSize outputSize,
            IScaler upscaler = null, IScaler downscaler = null, IScaler convolver = null)
            : this(inputFilter, outputSize, TextureChannels.All, Vector2.Zero, upscaler, downscaler, convolver)
        { }

        public ResizeFilter(IFilter<ITexture2D> inputFilter, TextureSize outputSize, TextureChannels channels, 
            IScaler upscaler = null, IScaler downscaler = null, IScaler convolver = null)
            : this(inputFilter, outputSize, channels, Vector2.Zero, upscaler, downscaler, convolver)
        { }

        public ResizeFilter(IFilter<ITexture2D> inputFilter, TextureSize outputSize, Vector2 offset, 
            IScaler upscaler = null, IScaler downscaler = null, IScaler convolver = null)
            : this(inputFilter, outputSize, TextureChannels.All, offset, upscaler, downscaler, convolver)
        { }

        public ResizeFilter(IFilter<ITexture2D> inputFilter, TextureSize outputSize, TextureChannels channels, Vector2 offset, 
            IScaler upscaler = null, IScaler downscaler = null, IScaler convolver = null)
            : base(inputFilter)
        {
            m_OutputSize = outputSize;
            m_Channels = channels;
            m_Offset = offset;

            m_Upscaler = upscaler ?? Renderer.LumaUpscaler;
            m_Downscaler = downscaler ?? Renderer.LumaDownscaler;
            m_Convolver = convolver;
        }

        private IFilter<ITexture2D> InputFilter
        {
            get { return (IFilter<ITexture2D>)InputFilters[0]; }
        }

        public void MakeTagged() 
        {
            m_Tagged = true;
        }

        public void ForceOffsetCorrection()
        {
            if (!m_Offset.IsZero)
                m_Convolver = m_Convolver ?? m_Upscaler;
        }

        public void SetSize(TextureSize targetSize)
        {
            m_OutputSize = targetSize;
        }

        protected override IFilter<ITexture2D> Optimize()
        {
            if (InputFilter.OutputSize == m_OutputSize && m_Convolver == null)
                return InputFilter;

            if (m_Tagged)
                AddTag(Status());

            return this;
        }

        public string Status()
        {
            var inputSize = InputFilters[0].OutputSize;
            return StatusHelpers.ScaleDescription(inputSize, OutputSize, m_Upscaler, m_Downscaler, m_Convolver);            
        }

        public override TextureSize OutputSize
        {
            get { return m_OutputSize; }
        }

        public override TextureFormat OutputFormat
        {
            get { return InputFilter.OutputFormat; }
        }

        protected override void Render(IList<IBaseTexture> inputs)
        {
            var texture = inputs.OfType<ITexture2D>().SingleOrDefault();
            if (texture == null)
                return;

            Renderer.Scale(OutputTarget, texture, m_Channels, m_Offset, m_Upscaler, m_Downscaler, m_Convolver);
        }
    }

    public sealed class MergeFilter : ShaderFilter
    {
        public MergeFilter(IFilter inputY, IFilter inputUv)
            : base(GetShader(true), inputY, inputUv)
        {
        }

        public MergeFilter(IFilter inputY, IFilter inputU, IFilter inputV)
            : base(GetShader(false), inputY, inputU, inputV)
        {
        }

        private static IShader GetShader(bool mergedUv)
        {
            var asmPath = typeof(IRenderScript).Assembly.Location;
            var shaderDataFilePath =
                Path.Combine(PathHelper.GetDirectoryName(asmPath),
                    "Extensions", "RenderScripts", "Common");
            var shaderFile = mergedUv ? "MergeY_UV.hlsl" : "MergeY_U_V.hlsl";
            return ShaderCache.CompileShader(Path.Combine(shaderDataFilePath, shaderFile));
        }
    }

    public static class ConversionHelper
    {
        public static IFilter ConvertToRgb(this IFilter filter)
        {
            return new RgbFilter(filter);
        }

        public static IFilter ConvertToYuv(this IFilter filter)
        {
            return new YuvFilter(filter);
        }

        public static IResizeableFilter Transform(this IResizeableFilter filter, Func<IFilter, IFilter> transformation)
        {
            return new TransformedResizeableFilter(transformation, filter);
        }

        public static IFilter Apply(this IFilter filter, Func<IFilter, IFilter> map)
        {
            return map(filter);
        }

        public static IFilter AddTaggedResizer(this IFilter<ITexture2D> filter)
        {
            return filter.SetSize(filter.OutputSize, true);
        }

        public static IFilter SetSize(this IFilter<ITexture2D> filter, TextureSize size, bool tagged = false)
        {
            var resizeable = (filter as IResizeableFilter) ?? new ResizeFilter(filter);
            if (tagged)
                resizeable.MakeTagged();
            resizeable.SetSize(size);
            return resizeable;
        }

        #region Auxilary class(es)

        private sealed class TransformedResizeableFilter : Filter, IResizeableFilter
        {
            private readonly IResizeableFilter m_InputFilter;
            private readonly Func<IFilter, IFilter> m_Transformation;

            public TransformedResizeableFilter(Func<IFilter, IFilter> transformation, IResizeableFilter inputFilter)
            {
                m_InputFilter = inputFilter;
                m_Transformation = transformation;
            }

            protected override IFilter<ITexture2D> Optimize()
            {
                var result = m_Transformation(m_InputFilter);

                if (m_InputFilter.OutputSize != result.OutputSize)
                    throw new InvalidOperationException("Transformation is not allowed to change the size.");
               
                return m_Transformation(m_InputFilter);
            }

            public void MakeTagged() 
            {
                m_InputFilter.MakeTagged();
            }

            public void SetSize(TextureSize outputSize)
            {
                m_InputFilter.SetSize(outputSize);
            }

            public override TextureSize OutputSize
            {
                get { return m_InputFilter.OutputSize; }
            }

            public override TextureFormat OutputFormat
            {
                get { return m_InputFilter.OutputFormat; }
            }

            protected override void Render(IList<IBaseTexture> inputs)
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }
}
