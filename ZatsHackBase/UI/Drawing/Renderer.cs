﻿using D3D11 = SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX.Direct3D;
using SharpDX.Mathematics.Interop;
using SharpDX.D3DCompiler;
using ZatsHackBase.Maths;
using SharpDX;
using ZatsHackBase.UI.Drawing;

namespace ZatsHackBase.UI
{
    //TODO: Implement
    public class Renderer : IDisposable
    {
        #region VARIABLES
        private D3D11.Device d3dDevice;
        private D3D11.DeviceContext d3dDeviceContext;
        private SwapChain swapChain;
        private D3D11.RenderTargetView renderTargetView;
        private D3D11.BlendState blendState;

        // Shaders
        private ShaderSet fontShader;
        private ShaderSet primitiveShader;

        private List<Font> fonts; 
        #endregion

        #region PROPERTIES
        public bool Initialized { get { return d3dDevice != null; } }
        public D3D11.Device Device => d3dDevice;
        public D3D11.DeviceContext DeviceContext => d3dDeviceContext;
        public GeometryBuffer GeometryBuffer { get; set; }
        public Size2F ViewportSize { get; set; }
        public Size2F hViewportSize { get; set; }
        #endregion

        #region CONSTRUCTORS
        public Renderer()
        {
        }
        #endregion

        #region METHODS
        #region RENDERER
        public void Init(Form form)
        {
            ModeDescription backBufferDesc = new ModeDescription(form.Width, form.Height, new Rational(60, 1), Format.R8G8B8A8_UNorm);
            SwapChainDescription swapChainDesc = new SwapChainDescription()
            {
                ModeDescription = backBufferDesc,
                SampleDescription = new SampleDescription(8, 0),
                Usage = Usage.RenderTargetOutput,
                BufferCount = 1,
                OutputHandle = form.Handle,
                IsWindowed = true
            };

            D3D11.Device.CreateWithSwapChain(DriverType.Hardware, D3D11.DeviceCreationFlags.None, swapChainDesc, out d3dDevice, out swapChain);
            d3dDeviceContext = d3dDevice.ImmediateContext;
            
            d3dDeviceContext.Rasterizer.SetViewport(0, 0, form.Width, form.Height);
            using (D3D11.Texture2D backBuffer = swapChain.GetBackBuffer<D3D11.Texture2D>(0))
            {
                renderTargetView = new D3D11.RenderTargetView(d3dDevice, backBuffer);
            }
            blendState = new D3D11.BlendState(d3dDevice, D3D11.BlendStateDescription.Default());

            GeometryBuffer = new GeometryBuffer(this);

            InitializeShaders();

            fonts = new List<Font>();

            ViewportSize = new Size2F(form.Width, form.Height);
            hViewportSize = new Size2F(form.Width/2f, form.Height/2f);
        }

        private void InitializeShaders()
        {
            //http://www.elitepvpers.com/forum/c-c/2936143-kreis-mithilfe-von-vertices-zeichnen.html#post25697120

            var primitiveShaderCode =
                @"
                struct Vertex
                {
                    float4 Origin : POSITION;
                    float4 Color  : COLOR;
                    float2 UV     : TEXCOORDS;
                };

                struct Pixel
                {
                    float4 Origin : SV_POSITION;
                    float4 Color  : COLOR;
                    float2 UV     : TEXCOORDS;
                };

                Pixel vertex_entry ( Vertex vertex )
                {
                    vertex.Origin.z = 0.0f;
                    vertex.Origin.w = 1.0f;

                    Pixel output;
                    
                    output.Origin   = vertex.Origin;
                    output.Color    = vertex.Color;
                    output.UV       = vertex.UV;
                    
                    return output;
                }

                float4 pixel_entry ( Pixel pixel ) : SV_TARGET
                {
                    return pixel.Color;
                }

                ";


            var fontShaderCode =
                @"
                struct Vertex
                {
                    float4 Origin : POSITION;
                    float4 Color  : COLOR;
                    float2 UV     : TEXCOORDS;
                };

                struct Pixel
                {
                    float4 Origin : SV_POSITION;
                    float4 Color  : COLOR;
                    float2 UV     : TEXCOORDS;
                };

                Pixel vertex_entry ( Vertex vertex )
                {
                    vertex.Origin.z = 0.0f;
                    vertex.Origin.w = 1.0f;

                    Pixel output;
                    
                    output.Origin   = vertex.Origin;
                    output.Color    = vertex.Color;
                    output.UV       = vertex.UV;
                    
                    return output;
                }

                Texture2D    g_texture : register( t0 );           
                SamplerState g_linearSampler : register( s0 );

                float4 pixel_entry ( Pixel pixel ) : SV_TARGET
                {
                    float4 texColor = g_texture.Sample ( g_linearSampler, pixel.UV );
                    float4 midvalue = texColor + pixel.Color;

                    midvalue.x = midvalue.x / 2.0f;
                    midvalue.y = midvalue.y / 2.0f;
                    midvalue.z = midvalue.z / 2.0f;

                    midvalue.w = texColor.w;

                    if ( texColor.w == 0.0f )
                    {
                        midvalue.x = 0.0f;
                        midvalue.y = 0.0f;
                        midvalue.z = 0.0f;
                    }

                    return midvalue;
                }

                ";

            primitiveShader = new ShaderSet(this,
                primitiveShaderCode, "vertex_entry", "pixel_entry", new[]
                {
                    new D3D11.InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                    new D3D11.InputElement("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
                    new D3D11.InputElement("TEXCOORDS", 0, Format.R32G32_Float, 32, 0),
                });

            fontShader = new ShaderSet(this,
                fontShaderCode, "vertex_entry", "pixel_entry", new[]
                {
                    new D3D11.InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                    new D3D11.InputElement("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
                    new D3D11.InputElement("TEXCOORDS", 0, Format.R32G32_Float, 32, 0),
                });

            fontShader.Apply();
        }
        
        public void Clear(Color color)
        {
            if (!Initialized)
                return;

            d3dDeviceContext.OutputMerger.SetRenderTargets(renderTargetView);
            d3dDeviceContext.OutputMerger.SetBlendState(blendState, new Color(0f, 0f, 0f, 1f), 0xFFFFFFFF);
            d3dDeviceContext.ClearRenderTargetView(renderTargetView, color);
            
        }

        public void Present()
        {
            if (!Initialized)
                return;
            
            //FillRectangle(new Color(1f, 1f, 0f, 0f), new Vector2(10f,100f), new Vector2(100f,100f));
            //testFont.DrawString(GeometryBuffer, new Vector2(10f, 10f), new RawColor4(1f, 0f, 1f, 1f), "test mo <3");

            //DrawString(new Color(1f, 0f, 1f, 0f), testFont, new Vector2(10f, 10f), "test mo <3");
            FillRectangle(new Color(1f, 1f,0f,0f), new Vector2(10f,50f), new Vector2(100f,100f) );;
            DrawRectangle(new Color(1f, 0f, 0f, 1f), new Vector2(10f, 160f), new Vector2(100f, 100f));

            GeometryBuffer.Draw();
            GeometryBuffer.Reset();

            swapChain.Present(1, PresentFlags.None);
        }

        public void Dispose()
        {
            if (!Initialized)
                return;

            renderTargetView.Dispose();
            swapChain.Dispose();
            d3dDevice.Dispose();
            d3dDeviceContext.Dispose();
        }
        #endregion

        #region RENDER FEATURES
        public Font CreateFont(string family, int height)
        {
            foreach (var fnt in fonts)
            {
                if (fnt.Family == family && fnt.Height == height)
                    return fnt;
            }
            var font = new Font(this, family, height, false, false);
            fonts.Add(font);
            return font;
        }

        public void DrawLine(Color color, Vector2 from, Vector2 to)
        {
            if (!Initialized)
                return;

            var col = (RawColor4)color;
            GeometryBuffer.AppendVertices(
                new Vertex(from.X,from.Y,col),
                new Vertex(to.X,to.Y,col)
            );

            //GeometryBuffer.AppendIndices(new short[]
            //{
            //    0,
            //    1
            //});

            GeometryBuffer.SetShader(primitiveShader);

            GeometryBuffer.DisableUseOfIndices();
            GeometryBuffer.SetPrimitiveType(PrimitiveTopology.LineList);

            GeometryBuffer.Trim();
        }

        public void DrawLines(Color color, Vector2[] points)
        {
            if (!Initialized)
                return;

            var col = (RawColor4)color;

            points.ToList().ForEach(el => { GeometryBuffer.AppendVertex(new Vertex { Origin = el, Color = col }); });

            

            GeometryBuffer.SetShader(primitiveShader);
            GeometryBuffer.SetPrimitiveType(PrimitiveTopology.LineList);
            GeometryBuffer.Trim();
        }

        public void FillRectangle(Color color, Vector2 location, Vector2 size)
        {
            if (!Initialized)
                return;

            var col = (RawColor4)color;

            GeometryBuffer.AppendVertices(
                new Vertex(location.X, location.Y, col),
                new Vertex(location.X + size.X, location.Y, col),
                new Vertex(location.X, location.Y + size.Y, col),
                new Vertex(location.X + size.X, location.Y + size.Y, col)
            );

            GeometryBuffer.AppendIndices(

                1,
                2,
                3,

                0,
                1,
                2

            );

            GeometryBuffer.SetShader(primitiveShader);
            GeometryBuffer.SetPrimitiveType(PrimitiveTopology.TriangleStrip);
            GeometryBuffer.Trim();
        }

        public void DrawRectangle(Color color, Vector2 location, Vector2 size)
        {
            if (!Initialized)
                return;

            var col = (RawColor4)color;
            GeometryBuffer.AppendVertices(
                new Vertex(location.X,          location.Y, col),

                new Vertex(location.X + size.X, location.Y, col),
                new Vertex(location.X,          location.Y + size.Y, col),

                new Vertex(location.X + size.X, location.Y + size.Y, col)
            );

            GeometryBuffer.AppendIndices(
                // top left -> top right
                0,
                1,

                // top right -> bottom right
                1,
                3,

                // bottom right -> bottom left
                3,
                2,

                // bottom left -> top left
                2,
                0
            );

            GeometryBuffer.SetShader(primitiveShader);
            GeometryBuffer.SetPrimitiveType(PrimitiveTopology.LineList);
            GeometryBuffer.Trim();
        }
        
        public void DrawString(Color color, Font font, Vector2 location, string text)
        {
            GeometryBuffer.SetShader(fontShader);
            font.DrawString(GeometryBuffer,location,(RawColor4)color,text);   
        }
        #endregion
        #endregion
    }
}
