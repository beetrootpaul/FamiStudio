﻿using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using Color = System.Drawing.Color;

using RenderBrush       = FamiStudio.GLBrush;
using RenderBitmap      = FamiStudio.GLBitmap;
using RenderBitmapAtlas = FamiStudio.GLBitmapAtlas;
using RenderFont        = FamiStudio.GLFont;
using RenderControl     = FamiStudio.GLControl;
using RenderGraphics    = FamiStudio.GLGraphics;
using RenderTheme       = FamiStudio.ThemeRenderResources;
using RenderCommandList = FamiStudio.GLCommandList;

namespace FamiStudio
{
    public class ProjectExplorer : RenderControl
    {
        const int DefaultExpandButtonSizeX    = 8;
        const int DefaultExpandButtonPosX     = 3;
        const int DefaultExpandButtonPosY     = 8;
        const int DefaultButtonIconPosX       = 3;
        const int DefaultButtonIconPosY       = 3;
        const int DefaultButtonTextPosX       = 21;
        const int DefaultButtonTextNoIconPosX = 4;
        const int DefaultSubButtonSpacingX    = PlatformUtils.IsMobile ? 17 : 18;
        const int DefaultSubButtonPosY        = 3;
        const int DefaultScrollBarThickness1  = 10;
        const int DefaultScrollBarThickness2  = 16;
        const int DefaultButtonSizeY          = 21;
        const int DefaultRegisterSizeY        = 14;
        const int DefaultSliderPosX           = PlatformUtils.IsMobile ? 88 : 108;
        const int DefaultSliderPosY           = 3;
        const int DefaultSliderSizeX          = PlatformUtils.IsMobile ? 84 : 104;
        const int DefaultSliderSizeY          = 15;
        const int DefaultCheckBoxPosX         = 20;
        const int DefaultCheckBoxPosY         = 3;
        const int DefaultDraggedLineSizeY     = 5;
        const int DefaultParamRightPadX       = 4;
        const int DefaultRegisterLabelSizeX   = 60;
        const float ScrollSpeedFactor         = PlatformUtils.IsMobile ? 2.0f : 1.0f;

        int expandButtonSizeX;
        int buttonIconPosX;
        int buttonIconPosY;
        int buttonTextPosX;
        int buttonTextNoIconPosX;
        int expandButtonPosX;
        int expandButtonPosY;
        int subButtonSpacingX;
        int subButtonPosY;
        int buttonSizeY;
        int sliderPosX;
        int sliderPosY;
        int sliderSizeX;
        int sliderSizeY;
        int checkBoxPosX;
        int checkBoxPosY;
        int paramRightPadX;
        int virtualSizeY;
        int scrollAreaSizeY;
        int scrollBarThickness;
        int draggedLineSizeY;
        int registerLabelSizeX;
        int contentSizeX;
        int topTabSizeY;
        bool needsScrollBar;

        enum TabType
        {
            Project,
            Registers,
            Count
        };

        string[] TabNames =
        {
            "Project",
            "Registers"
        };

        delegate object GetRegisterValueDelegate();
        delegate void   DrawRegisterDelegate(RenderCommandList c, ThemeRenderResources res, Rectangle rect);

        public static readonly string[] NoteNamesPadded =
{
            "C-",
            "C#",
            "D-",
            "D#",
            "E-",
            "F-",
            "F#",
            "G-",
            "G#",
            "A-",
            "A#",
            "B-"
        };

        //private static double N163PeriodToFreq(int cpuFreq, int period, int waveLen)
        //{
        //    return cpuFreq / 983040.0 * period / waveLen / 4;
        //}

        private static double NoteFromFreq(double f)
        {
            return 12.0 * Math.Log(f / NesApu.FreqC0, 2.0);
        }

        private static string GetNoteString(int value)
        {
            int octave = (value - 1) / 12;
            int note = (value - 1) % 12;

            return $"{NoteNamesPadded[note]}{octave}";
        }

        private static string GetPitchString(int period, double frequency)
        {
            if (period == 0 || frequency < NesApu.FreqC0)
            {
                return $"---+{Math.Abs(0):00} ({0,7:0.00}Hz)";
            }
            else
            {
                var noteFloat = NoteFromFreq(frequency);
                Debug.Assert(noteFloat >= 0);

                var note = (int)Math.Round(noteFloat);
                var cents = (int)Math.Round((noteFloat - note) * 100.0);

                return $"{GetNoteString(note + 1),-3}{(cents < 0 ? "-" : "+")}{Math.Abs(cents):00} ({frequency,7:0.00}Hz)";
            }
        }

        class RegisterViewerRow
        {
            public string Label;
            public int Height = DefaultRegisterSizeY;
            public int AddStart;
            public int AddEnd;
            public int SubStart;
            public int SubEnd;
            public bool Monospace;
            public GetRegisterValueDelegate GetValue;
            public DrawRegisterDelegate CustomDraw;

            // Address range.
            public RegisterViewerRow(string label, int addStart, int addEnd)
            {
                Label = label;
                AddStart = addStart;
                AddEnd = addEnd;
            }

            // Address range (for internal registers)
            public RegisterViewerRow(string label, int address, int subStart, int subEnd)
            {
                Label = label;
                AddStart = address;
                AddEnd = address;
                SubStart = subStart;
                SubEnd = subEnd;
            }

            // Text label.
            public RegisterViewerRow(string label, GetRegisterValueDelegate value, bool mono = false)
            {
                Label = label;
                GetValue = value;
                Monospace = mono;
            }

            // Custom draw.
            public RegisterViewerRow(string label, DrawRegisterDelegate draw, int height = DefaultRegisterSizeY)
            {
                Label = label;
                Height = height;
                CustomDraw = draw;
            }
        };

        class ExpansionRegisterViewer
        {
            public RegisterViewerRow[]   ExpansionRows { get; internal set; }
            public RegisterViewerRow[][] ChannelRows { get; internal set; }
        }

        class ApuRegisterViewer : ExpansionRegisterViewer
        {
            ApuRegisterInterpreter i;

            public ApuRegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new ApuRegisterInterpreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$4000", 0x4000, 0x4003),
                    new RegisterViewerRow("$4004", 0x4004, 0x4007),
                    new RegisterViewerRow("$4008", 0x4008, 0x400b),
                    new RegisterViewerRow("$400C", 0x400c, 0x400f),
                    new RegisterViewerRow("$4010", 0x4010, 0x4013)
                };
                ChannelRows = new RegisterViewerRow[5][];
                ChannelRows[0] = new[]
                {
                    new RegisterViewerRow("Pitch",      () => GetPitchString(i.GetSquarePeriod(0), i.GetSquareFrequency(0)), true),
                    new RegisterViewerRow("Volume",     () => i.GetSquareVolume(0).ToString("00"), true),
                    new RegisterViewerRow("Duty",       () => i.GetSquareDuty(0), true)
                };                                      
                ChannelRows[1] = new[]                  
                {                                       
                    new RegisterViewerRow("Pitch",      () => GetPitchString(i.GetSquarePeriod(1), i.GetSquareFrequency(1)), true),
                    new RegisterViewerRow("Volume",     () => i.GetSquareVolume(1).ToString("00"), true),
                    new RegisterViewerRow("Duty",       () => i.GetSquareDuty(1), true)
                };                                      
                ChannelRows[2] = new[]                  
                {                                       
                    new RegisterViewerRow("Pitch",      () => GetPitchString(i.TrianglePeriod, i.TriangleFrequency), true),
                };                                      
                ChannelRows[3] = new[]                  
                {                                       
                    new RegisterViewerRow("Pitch",      () => i.NoisePeriod.ToString("X"), true),
                    new RegisterViewerRow("Volume",     () => i.NoiseVolume.ToString("00"), true),
                    new RegisterViewerRow("Mode",       () => i.NoiseMode, true)
                };
                ChannelRows[4] = new[]
                {
                    new RegisterViewerRow("Frequency",  () => DPCMSampleRate.GetString(false, r.Pal, true, true, i.DpcmFrequency), true),
                    new RegisterViewerRow("Loop",       () => i.DpcmLoop ? "Loop" : "Once", false),
                    new RegisterViewerRow("Size",       () => i.DpcmSize, true),
                    new RegisterViewerRow("Bytes Left", () => i.DpcmBytesLeft, true),
                    new RegisterViewerRow("DAC",        () => i.DpcmDac, true)
                };
            }
        }

        class Vrc6RegisterViewer : ExpansionRegisterViewer
        {
            Vrc6RegisterInterpreter i;

            public Vrc6RegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new Vrc6RegisterInterpreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$9000", 0x9000, 0x9002),
                    new RegisterViewerRow("$A000", 0xA000, 0xA002),
                    new RegisterViewerRow("$B000", 0xB000, 0xB002)
                };
                ChannelRows = new RegisterViewerRow[3][];
                ChannelRows[0] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetSquarePeriod(0), i.GetSquareFrequency(0)), true),
                    new RegisterViewerRow("Volume", () => i.GetSquareVolume(0).ToString("00"), true),
                    new RegisterViewerRow("Duty",   () => i.GetSquareDuty(0), true)
                };
                ChannelRows[1] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetSquarePeriod(1), i.GetSquareFrequency(1)), true),
                    new RegisterViewerRow("Volume", () => i.GetSquareVolume(1).ToString("00"), true),
                    new RegisterViewerRow("Duty",   () => i.GetSquareDuty(1), true)
                };
                ChannelRows[2] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.SawPeriod, i.SawFrequency), true),
                    new RegisterViewerRow("Volume", () => i.SawVolume.ToString("00"), true),
                };
            }
        }

        class Vrc7RegisterViewer : ExpansionRegisterViewer
        {
            Vrc7RegisterIntepreter i;

            public Vrc7RegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new Vrc7RegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$10", 0x9030, 0x10, 0x16),
                    new RegisterViewerRow("$20", 0x9030, 0x20, 0x26),
                    new RegisterViewerRow("$30", 0x9030, 0x30, 0x36)
                };
                ChannelRows = new RegisterViewerRow[6][];
                for (int j = 0; j < 6; j++)
                {
                    var c = j; // Important, need to make a copy for the lambda.
                    ChannelRows[c] = new[]
                    {
                        new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetPeriod(c), i.GetFrequency(c)), true),
                        new RegisterViewerRow("Volume", () => i.GetVolume(c).ToString("00"), true),
                        new RegisterViewerRow("Patch",  () => i.GetPatch(c), true),
                    };
                }
            }
        }

        class FdsRegisterViewer : ExpansionRegisterViewer
        {
            FdsRegisterIntepreter i;

            public FdsRegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new FdsRegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$4080", 0x4080, 0x4083),
                    new RegisterViewerRow("$4084", 0x4084, 0x4087),
                    new RegisterViewerRow("$4088", 0x4088, 0x408b),
                };
                ChannelRows = new RegisterViewerRow[1][];
                ChannelRows[0] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.Period, i.Frequency), true),
                    new RegisterViewerRow("Volume", () => i.Volume.ToString("00"), true),
                    new RegisterViewerRow("Wave", DrawWaveTable, 32),
                    new RegisterViewerRow("Mod", DrawModTable, 32),
                };
            }

            void DrawInternal(RenderCommandList c, ThemeRenderResources res, Rectangle rect, byte[] vals, int maxVal, bool signed)
            {
                var sx = rect.Width  / 64;
                var sy = rect.Height / (float)maxVal;
                var h = rect.Height;

                for (int x = 0; x < 64; x++)
                {
                    var y = vals[x] * sy;
                    var color = i.Registers.InstrumentColors[ChannelType.FdsWave];

                    if (color.A == 0)
                        color = Theme.LightGreyFillColor2;

                    var brush = c.Graphics.GetSolidBrush(color);

                    if (signed)
                        c.FillRectangle(x * sx, h - y, (x + 1) * sx, h / 2, brush);
                    else
                        c.FillRectangle(x * sx, h - y, (x + 1) * sx, h, brush);
                }

                c.FillRectangle(64 * sx, 0, 64 * sx, rect.Height, res.DarkGreyLineBrush3);
                c.DrawLine(64 * sx, 0, 64 * sx, rect.Height, res.BlackBrush);
            }

            void DrawWaveTable(RenderCommandList c, ThemeRenderResources res, Rectangle rect)
            {
                DrawInternal(c, res, rect, i.GetWaveTable(), 63, true);
            }

            void DrawModTable(RenderCommandList c, ThemeRenderResources res, Rectangle rect)
            {
                DrawInternal(c, res, rect, i.GetModTable(), 7, false);
            }
        }

        class Mmc5RegisterViewer : ExpansionRegisterViewer
        {
            Mmc5RegisterIntepreter i;

            public Mmc5RegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new Mmc5RegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$5000", 0x5000, 0x5003),
                    new RegisterViewerRow("$5004", 0x5004, 0x5007),
                };
                ChannelRows = new RegisterViewerRow[2][];
                ChannelRows[0] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetSquarePeriod(0), i.GetSquareFrequency(0)), true),
                    new RegisterViewerRow("Volume", () => i.GetSquareVolume(0).ToString("00"), true),
                    new RegisterViewerRow("Duty",   () => i.GetSquareDuty(0), true)
                };
                ChannelRows[1] = new[]
                {
                    new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetSquarePeriod(1), i.GetSquareFrequency(1)), true),
                    new RegisterViewerRow("Volume", () => i.GetSquareVolume(1).ToString("00"), true),
                    new RegisterViewerRow("Duty",   () => i.GetSquareDuty(1), true)
                };

            }
        }

        class N163RegisterViewer : ExpansionRegisterViewer
        {
            N163RegisterIntepreter i;

            public N163RegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new N163RegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$00", 0x4800, 0x00, 0x07 ),
                    new RegisterViewerRow("$08", 0x4800, 0x08, 0x0f ),
                    new RegisterViewerRow("$10", 0x4800, 0x10, 0x17 ),
                    new RegisterViewerRow("$18", 0x4800, 0x18, 0x1f ),
                    new RegisterViewerRow("$20", 0x4800, 0x20, 0x27 ),
                    new RegisterViewerRow("$28", 0x4800, 0x28, 0x2f ),
                    new RegisterViewerRow("$30", 0x4800, 0x30, 0x37 ),
                    new RegisterViewerRow("$38", 0x4800, 0x38, 0x3f ),
                    new RegisterViewerRow("$40", 0x4800, 0x40, 0x47 ),
                    new RegisterViewerRow("$48", 0x4800, 0x48, 0x4f ),
                    new RegisterViewerRow("$50", 0x4800, 0x50, 0x57 ),
                    new RegisterViewerRow("$58", 0x4800, 0x58, 0x5f ),
                    new RegisterViewerRow("$60", 0x4800, 0x60, 0x67 ),
                    new RegisterViewerRow("$68", 0x4800, 0x68, 0x6f ),
                    new RegisterViewerRow("$70", 0x4800, 0x70, 0x77 ),
                    new RegisterViewerRow("$78", 0x4800, 0x78, 0x7f ),
                    new RegisterViewerRow("RAM", DrawRamMap, 32),
                };
                ChannelRows = new RegisterViewerRow[8][];
                for (int j = 0; j < 8; j++)
                {
                    var c = j; // Important, need to make a copy for the lambda.
                    ChannelRows[c] = new[]
                    {
                        new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetPeriod(c), i.GetFrequency(c)), true),
                        new RegisterViewerRow("Volume", () => i.GetVolume(c).ToString("00"), true),
                    };
                }
            }

            void DrawRamMap(RenderCommandList c, ThemeRenderResources res, Rectangle rect)
            {
                var ramSize   = 128 - i.NumActiveChannels * 8;
                var numValues = ramSize * 2;

                var sx = rect.Width  / numValues;
                var sy = rect.Height / 15.0f;
                var h  = rect.Height;

                for (int x = 0; x < ramSize; x++)
                {
                    var val = i.Registers.GetRegisterValue(ExpansionType.N163, NesApu.N163_DATA, x);
                    var lo = ((val >> 0) & 0xf) * sy;
                    var hi = ((val >> 4) & 0xf) * sy;
                    
                    // See if the RAM address matches any of the instrument.
                    // This isn't very accurate since we don't actually know
                    // which instrument last wrote to RAM at the moment, but
                    // it will work when there is no overlap.
                    var channelIndex = -1;
                    for (int j = 0; j < i.NumActiveChannels; j++)
                    {
                        if (x * 2 >= i.Registers.N163InstrumentRanges[j].Position &&
                            x * 2 <  i.Registers.N163InstrumentRanges[j].Position + i.Registers.N163InstrumentRanges[j].Size)
                        {
                            channelIndex = j;
                            break;
                        }
                    }

                    var color = channelIndex >= 0 ? i.Registers.InstrumentColors[ChannelType.N163Wave1 + channelIndex] : Theme.LightGreyFillColor2;
                    var brush = c.Graphics.GetSolidBrush(color);

                    c.FillRectangle((x * 2 + 0) * sx, h - lo, (x * 2 + 1) * sx, h, brush);
                    c.FillRectangle((x * 2 + 1) * sx, h - hi, (x * 2 + 2) * sx, h, brush);
                }

                c.FillRectangle(numValues * sx, 0, 256 * sx, rect.Height, res.DarkGreyLineBrush3);
                c.DrawLine(256 * sx, 0, 256 * sx, rect.Height, res.BlackBrush);
            }
        }

        class S5BRegisterViewer : ExpansionRegisterViewer
        {
            S5BRegisterIntepreter i;

            public S5BRegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new S5BRegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$00", 0xE000, 0x00, 0x01),
                    new RegisterViewerRow("$02", 0xE000, 0x02, 0x03),
                    new RegisterViewerRow("$04", 0xE000, 0x04, 0x05),
                    new RegisterViewerRow("$08", 0xE000, 0x08, 0x0a),
                };
                ChannelRows = new RegisterViewerRow[3][];
                for (int j = 0; j < 3; j++)
                {
                    var c = j; // Important, need to make a copy for the lambda.
                    ChannelRows[c] = new[]
                    {
                        new RegisterViewerRow("Pitch",  () => GetPitchString(i.GetPeriod(c), i.GetFrequency(c)), true),
                        new RegisterViewerRow("Volume", () => i.GetVolume(c).ToString("00"), true),
                    };
                }
            }
        }

        class EpsmRegisterViewer : ExpansionRegisterViewer
        {
            EpsmRegisterIntepreter i;

            public EpsmRegisterViewer(NesApu.NesRegisterValues r)
            {
                i = new EpsmRegisterIntepreter(r);
                ExpansionRows = new[]
                {
                    new RegisterViewerRow("$00", 0x401d, 0x00, 0x01),
                    new RegisterViewerRow("$02", 0x401d, 0x02, 0x03),
                    new RegisterViewerRow("$04", 0x401d, 0x04, 0x05),
                    new RegisterViewerRow("$08", 0x401d, 0x08, 0x0a),
                    new RegisterViewerRow("$10", 0x401d, 0x10, 0x12),
                    new RegisterViewerRow("$18", 0x401d, 0x18, 0x1d),
                    new RegisterViewerRow("$22", 0x401d, 0x22, 0x22),
                    new RegisterViewerRow("$28", 0x401d, 0x28, 0x28),
                    new RegisterViewerRow("$30 A0", 0x401d, 0x30, 0x37),
                    new RegisterViewerRow("$30 A0", 0x401d, 0x38, 0x3f),
                    new RegisterViewerRow("$40 A0", 0x401d, 0x40, 0x47),
                    new RegisterViewerRow("$40 A0", 0x401d, 0x48, 0x4f),
                    new RegisterViewerRow("$50 A0", 0x401d, 0x50, 0x57),
                    new RegisterViewerRow("$50 A0", 0x401d, 0x58, 0x5f),
                    new RegisterViewerRow("$60 A0", 0x401d, 0x60, 0x67),
                    new RegisterViewerRow("$60 A0", 0x401d, 0x68, 0x6f),
                    new RegisterViewerRow("$70 A0", 0x401d, 0x70, 0x77),
                    new RegisterViewerRow("$70 A0", 0x401d, 0x78, 0x7f),
                    new RegisterViewerRow("$80 A0", 0x401d, 0x80, 0x87),
                    new RegisterViewerRow("$80 A0", 0x401d, 0x88, 0x8f),
                    new RegisterViewerRow("$90 A0", 0x401d, 0x90, 0x97),
                    new RegisterViewerRow("$90 A0", 0x401d, 0x98, 0x9f),
                    new RegisterViewerRow("$A0 A0", 0x401d, 0xa0, 0xa7),
                    new RegisterViewerRow("$B0 A0", 0x401d, 0xb0, 0xb7),
                    new RegisterViewerRow("$30 A1", 0x401f, 0x30, 0x37),
                    new RegisterViewerRow("$30 A1", 0x401f, 0x38, 0x3f),
                    new RegisterViewerRow("$40 A1", 0x401f, 0x40, 0x47),
                    new RegisterViewerRow("$40 A1", 0x401f, 0x48, 0x4f),
                    new RegisterViewerRow("$50 A1", 0x401f, 0x50, 0x57),
                    new RegisterViewerRow("$50 A1", 0x401f, 0x58, 0x5f),
                    new RegisterViewerRow("$60 A1", 0x401f, 0x60, 0x67),
                    new RegisterViewerRow("$60 A1", 0x401f, 0x68, 0x6f),
                    new RegisterViewerRow("$70 A1", 0x401f, 0x70, 0x77),
                    new RegisterViewerRow("$70 A1", 0x401f, 0x78, 0x7f),
                    new RegisterViewerRow("$80 A1", 0x401f, 0x80, 0x87),
                    new RegisterViewerRow("$80 A1", 0x401f, 0x88, 0x8f),
                    new RegisterViewerRow("$90 A1", 0x401f, 0x90, 0x97),
                    new RegisterViewerRow("$90 A1", 0x401f, 0x98, 0x9f),
                    new RegisterViewerRow("$A0 A1", 0x401f, 0xa0, 0xa7),
                    new RegisterViewerRow("$B0 A1", 0x401f, 0xb0, 0xb7),
                };
                ChannelRows = new RegisterViewerRow[15][];
                for (int j = 0; j < 15; j++)
                {
                    var c = j; // Important, need to make a copy for the lambda.
                    if (j < 3)
                        ChannelRows[c] = new[]
                        {
                            new RegisterViewerRow("Pitch", () => GetPitchString(i.GetPeriod(c), i.GetFrequency(c)), true),
                            new RegisterViewerRow("Volume", () => i.GetVolume(c).ToString("00"), true),
                        };
                    if (j >= 3 && j < 9)
                        ChannelRows[c] = new[]
                        {
                            new RegisterViewerRow("Pitch", () => GetPitchString(i.GetPeriod(c), i.GetFrequency(c)), true),
                            //new RegisterViewerRow("Pitch", () =>  i.GetFrequency(c), true),
                            new RegisterViewerRow("Stereo", () => i.GetStereo(c), true),
                            new RegisterViewerRow("Vol OP1", () => i.GetVolume(c,0).ToString("00"), true),
                            new RegisterViewerRow("Vol OP2", () => i.GetVolume(c,2).ToString("00"), true),
                            new RegisterViewerRow("Vol OP3", () => i.GetVolume(c,1).ToString("00"), true),
                            new RegisterViewerRow("Vol OP4", () => i.GetVolume(c,3).ToString("00"), true),
                        };
                    if (j >= 9 )
                        ChannelRows[c] = new[]
                        {
                            new RegisterViewerRow("Stereo", () => i.GetStereo(c), true),
                            new RegisterViewerRow("Volume", () => i.GetVolume(c).ToString("00"), true),
                        };
                }
            }
        }

        enum ButtonType
        {
            // Project explorer buttons.
            ProjectSettings,
            SongHeader,
            Song,
            InstrumentHeader,
            Instrument,
            DpcmHeader,
            Dpcm,
            ArpeggioHeader,
            Arpeggio,
            ParamTabs,
            ParamCheckbox,
            ParamSlider,
            ParamList,
            ParamCustomDraw,

            // Register viewer buttons.
            RegisterExpansionHeader,
            RegisterChannelHeader,
            ExpansionRegistersFirst, // One type per expansion for raw registers.
            ChannelStateFirst = ExpansionRegistersFirst + ExpansionType.Count, // One type per channel for status.

            Max = ChannelStateFirst + ChannelType.Count
        };

        enum SubButtonType
        {
            // Let's keep this enum and Envelope.XXX values in sync for convenience.
            VolumeEnvelope        = EnvelopeType.Volume,
            ArpeggioEnvelope      = EnvelopeType.Arpeggio,
            PitchEnvelope         = EnvelopeType.Pitch,
            DutyCycle             = EnvelopeType.DutyCycle,
            FdsWaveformEnvelope   = EnvelopeType.FdsWaveform,
            FdsModulationEnvelope = EnvelopeType.FdsModulation,
            N163WaveformEnvelope  = EnvelopeType.N163Waveform,
            EnvelopeMax           = EnvelopeType.Count,

            // Other buttons
            Add,
            DPCM,
            Load,
            Save,
            Reload,
            EditWave,
            Play,
            Expand,
            Overflow,
            Properties,
            Max
        }

        // From right to left. Looks more visually pleasing than the enum order.
        static readonly int[] EnvelopeDisplayOrder =
        {
            EnvelopeType.Arpeggio,
            EnvelopeType.Pitch,
            EnvelopeType.Volume,
            EnvelopeType.DutyCycle,
            EnvelopeType.FdsModulation,
            EnvelopeType.FdsWaveform,
            EnvelopeType.N163Waveform
        };

        enum CaptureOperation
        {
            None,
            DragInstrument,
            DragArpeggio,
            DragSample,
            DragSong,
            MoveSlider,
            ScrollBar,
            MobilePan
        };

        static readonly bool[] captureNeedsThreshold = new[]
        {
            false, // None,
            true,  // DragInstrument,
            true,  // DragArpeggio,
            false, // DragSample,
            true,  // DragSong,
            false, // MoveSlider,
            false, // ScrollBar
            false  // MobilePan
        };

        static readonly bool[] captureWantsRealTimeUpdate = new[]
        {
            false, // None,
            true,  // DragInstrument,
            false, // DragArpeggio,
            false, // DragSample,
            true,  // DragSong,
            false, // MoveSlider,
            false, // ScrollBar
            false  // MobilePan
        };

        int scrollY = 0;
        int mouseLastX = 0;
        int mouseLastY = 0;
        int captureMouseX = -1;
        int captureMouseY = -1;
        int captureButtonRelX = -1;
        int captureButtonRelY = -1;
        int captureButtonIdx = -1;
        int captureScrollY = -1;
        int envelopeDragIdx = -1;
        int highlightedButtonIdx = -1;
        float flingVelY = 0.0f;
        float bitmapScale = 1.0f;
        bool captureThresholdMet = false;
        bool captureRealTimeUpdate = false;
        bool canFling = false;
        TabType selectedTab = TabType.Project;
        Button sliderDragButton = null;
        CaptureOperation captureOperation = CaptureOperation.None;
        Instrument draggedInstrument = null;
        Instrument expandedInstrument = null;
        string selectedInstrumentTab = null;
        DPCMSample expandedSample = null;
        Arpeggio draggedArpeggio = null;
        DPCMSample draggedSample = null;
        Song draggedSong = null;
        List<Button> buttons = new List<Button>();

        // Register viewer stuff
        NesApu.NesRegisterValues registerValues;
        ExpansionRegisterViewer[] registerViewers = new ExpansionRegisterViewer[ExpansionType.Count];

        RenderBrush sliderFillBrush;
        RenderBrush   disabledBrush;
        RenderBrush[] registerBrushes = new RenderBrush[11];
        RenderBitmapAtlas bmpMiscAtlas;
        RenderBitmapAtlas bmpExpansionsAtlas;
        RenderBitmapAtlas bmpEnvelopesAtlas;
        RenderBitmapAtlas bmpAtlasChannels;

        enum MiscImageIndices
        {
            Expand,
            Expanded,
            Overflow,
            CheckBoxYes,
            CheckBoxNo,
            ButtonLeft,
            ButtonRight,
            Song,
            Add,
            Play,
            DPCM,
            Load,
            WaveEdit,
            Reload,
            Save,
            Properties,
            Count
        };

        readonly string[] MiscImageNames = new string[]
        {
            "InstrumentExpand",
            "InstrumentExpanded",
            "Warning",
            "CheckBoxYes",
            "CheckBoxNo",
            "ButtonLeft",
            "ButtonRight",
            "Music",
            "Add",
            "PlaySource",
            "ChannelDPCM",
            "InstrumentOpen",
            "WaveEdit",
            "Reload",
            "SaveSmall",
            "Properties"
        };

        enum EnvelopesImageIndices
        {
            Volume,
            Arpeggio,
            Pitch,
            Duty,
            FdsWave,
            Mod,
            N163Wave,
            Count
        };

        readonly string[] EnvelopesImageNames = new string[]
        {
            "EnvelopeVolume",
            "EnvelopeArpeggio",
            "EnvelopePitch",
            "EnvelopeDuty",
            "EnvelopeWave",
            "EnvelopeMod",
            "EnvelopeWave",
        };

        class Button
        {
            public string text;
            public Color color = Theme.DarkGreyFillColor2;
            public Color imageTint = Color.Black;
            public RenderBrush textBrush;
            public RenderBrush textDisabledBrush;
            public RenderBitmapAtlas atlas;
            public int atlasIdx;
            public int height;
            public bool gradient = true;
            public string[] tabNames;
            public RegisterViewerRow[] regs;

            public ButtonType type;
            public Song song;
            public Instrument instrument;
            public Arpeggio arpeggio;
            public DPCMSample sample;
            public ProjectExplorer projectExplorer;

            public ParamInfo param;
            public TransactionScope paramScope;
            public int paramObjectId;

            public Button(ProjectExplorer pe)
            {
                projectExplorer = pe;
                textBrush = pe.ThemeResources.LightGreyFillBrush2;
                textDisabledBrush = pe.disabledBrush;
                height = pe.buttonSizeY;
            }

            public SubButtonType[] GetSubButtons(out int active)
            {
                active = -1;

                switch (type)
                {
                    case ButtonType.SongHeader:
                    case ButtonType.InstrumentHeader:
                        return new[] { SubButtonType.Add, SubButtonType.Load };
                    case ButtonType.ArpeggioHeader:
                        return new[] { SubButtonType.Add };
                    case ButtonType.DpcmHeader:
                        return new[] { SubButtonType.Load };
                    case ButtonType.ProjectSettings:
                    case ButtonType.Song:
                        return new[] { SubButtonType.Properties };
                    case ButtonType.Instrument:
                        if (instrument == null)
                        {
                            var project = projectExplorer.App.Project;
                            if (project != null && project.GetTotalMappedSampleSize() > Project.MaxMappedSampleSize)
                                return new[] { SubButtonType.DPCM, SubButtonType.Overflow };
                            else
                                return new[] { SubButtonType.DPCM };
                        }
                        else
                        {
                            var expandButton = projectExplorer.ShowExpandButtons() && InstrumentParamProvider.HasParams(instrument);
                            var numSubButtons = instrument.NumActiveEnvelopes + (expandButton ? 1 : 0) + 1;
                            var buttons = new SubButtonType[numSubButtons];
                            buttons[0] = SubButtonType.Properties;

                            for (int i = 0, j = 1; i < EnvelopeType.Count; i++)
                            {
                                int idx = EnvelopeDisplayOrder[i];
                                if (instrument.Envelopes[idx] != null)
                                {
                                    buttons[j] = (SubButtonType)idx;
                                    if (instrument.Envelopes[idx].IsEmpty(idx))
                                        active &= ~(1 << j);
                                    j++;
                                }
                            }

                            if (expandButton)
                                buttons[numSubButtons - 1] = SubButtonType.Expand;

                            return buttons;
                        }
                    case ButtonType.Arpeggio:
                        if (arpeggio != null)
                            return new[] { SubButtonType.Properties, SubButtonType.ArpeggioEnvelope };
                        break;
                    case ButtonType.Dpcm:
                        if (PlatformUtils.IsMobile)
                        {
                            return new[] { SubButtonType.Properties, SubButtonType.EditWave, SubButtonType.Play, SubButtonType.Expand };
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(sample.SourceFilename))
                                active &= ~(1 << 3);
                            return new[] { SubButtonType.Properties, SubButtonType.EditWave, SubButtonType.Save, SubButtonType.Reload, SubButtonType.Play, SubButtonType.Expand };
                        }
                }

                return null;
            }

            public bool IsParam
            {
                get
                {
                    return
                        type == ButtonType.ParamCheckbox ||
                        type == ButtonType.ParamSlider ||
                        type == ButtonType.ParamCustomDraw ||
                        type == ButtonType.ParamList ||
                        type == ButtonType.ParamTabs;
                }
            }

            public string Text
            {
                get
                {
                    if (text != null)
                    {
                        return text;
                    }
                    else if (type == ButtonType.Instrument && instrument == null)
                    {
                        var label = Project.DPCMInstrumentName;
                        if (projectExplorer.App.Project != null)
                        {
                            var mappedSamplesSize = projectExplorer.App.Project.GetTotalMappedSampleSize();
                            if (mappedSamplesSize > 0)
                                label += $" ({mappedSamplesSize} Bytes)";
                        }
                        return label;
                    }
                    else if (type == ButtonType.Dpcm)
                    {
                        return $"{sample.Name} ({sample.ProcessedData.Length} Bytes)"; 
                    }
                    else if (type == ButtonType.DpcmHeader)
                    {
                        var label = "DPCM Samples";
                        // Not useful info.
                        //if (projectExplorer.App.Project != null)
                        //{
                        //    var samplesSize = projectExplorer.App.Project.GetTotalSampleSize();
                        //    if (samplesSize > 0)
                        //        label += $" ({samplesSize} Bytes)";
                        //}
                        return label;
                    }

                    return "";
                }
            }

            public RenderFont Font
            {
                get
                {
                    if ((type == ButtonType.Song       && song       == projectExplorer.App.SelectedSong)       ||
                        (type == ButtonType.Instrument && instrument == projectExplorer.App.SelectedInstrument) ||
                        (type == ButtonType.Arpeggio   && arpeggio   == projectExplorer.App.SelectedArpeggio))
                    {
                        return projectExplorer.ThemeResources.FontMediumBold;
                    }
                    else if (
                        type == ButtonType.ProjectSettings         ||
                        type == ButtonType.SongHeader              ||
                        type == ButtonType.InstrumentHeader        ||
                        type == ButtonType.DpcmHeader              ||
                        type == ButtonType.ArpeggioHeader          ||
                        type == ButtonType.RegisterExpansionHeader ||
                        type == ButtonType.RegisterChannelHeader)
                    {
                        return projectExplorer.ThemeResources.FontMediumBold;
                    }
                    else
                    {
                        return projectExplorer.ThemeResources.FontMedium;
                    }
                }
            }

            public RenderTextFlags TextAlignment
            {
                get
                {
                    if (type == ButtonType.ProjectSettings ||
                        type == ButtonType.SongHeader ||
                        type == ButtonType.InstrumentHeader ||
                        type == ButtonType.DpcmHeader ||
                        type == ButtonType.ArpeggioHeader)
                    {
                        return RenderTextFlags.Center;
                    }
                    else
                    {
                        return RenderTextFlags.Left;
                    }
                }
            }

            public Color SubButtonTint => type == ButtonType.SongHeader || type == ButtonType.InstrumentHeader || type == ButtonType.DpcmHeader || type == ButtonType.ArpeggioHeader || type == ButtonType.ProjectSettings ? Theme.LightGreyFillColor1 : Color.Black;

            public bool TextEllipsis => type == ButtonType.ProjectSettings;
            
            public RenderBitmapAtlas GetIcon(SubButtonType sub, out int atlasIdx)
            {
                switch (sub)
                {
                    case SubButtonType.Add: 
                        atlasIdx = (int)MiscImageIndices.Add;  
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Play: 
                        atlasIdx = (int)MiscImageIndices.Play; 
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Save:
                        atlasIdx = (int)MiscImageIndices.Save; 
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.DPCM: 
                        atlasIdx = (int)MiscImageIndices.DPCM; 
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.EditWave:
                        atlasIdx = (int)MiscImageIndices.WaveEdit; 
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Reload: 
                        atlasIdx = (int)MiscImageIndices.Reload;
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Load: 
                        atlasIdx = (int)MiscImageIndices.Load;
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Overflow: 
                        atlasIdx = (int)MiscImageIndices.Overflow;
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Properties:
                        atlasIdx = (int)MiscImageIndices.Properties;
                        return projectExplorer.bmpMiscAtlas;
                    case SubButtonType.Expand:
                    {
                        if (instrument != null)
                            atlasIdx = projectExplorer.expandedInstrument == instrument ? (int)MiscImageIndices.Expanded : (int)MiscImageIndices.Expand;
                        else
                            atlasIdx = projectExplorer.expandedSample == sample ? (int)MiscImageIndices.Expanded : (int)MiscImageIndices.Expand;

                        return projectExplorer.bmpMiscAtlas;
                    }
                }

                atlasIdx = (int)sub;
                return projectExplorer.bmpEnvelopesAtlas;
            }
        }

        public DPCMSample DraggedSample => captureOperation == CaptureOperation.DragSample ? draggedSample : null;

        public delegate void EmptyDelegate();
        public delegate void BoolDelegate(bool val);
        public delegate void InstrumentEnvelopeDelegate(Instrument instrument, int envelope);
        public delegate void InstrumentDelegate(Instrument instrument);
        public delegate void InstrumentPointDelegate(Instrument instrument, Point pos);
        public delegate void SongDelegate(Song song);
        public delegate void ArpeggioDelegate(Arpeggio arpeggio);
        public delegate void ArpeggioPointDelegate(Arpeggio arpeggio, Point pos);
        public delegate void DPCMSamplePointDelegate(DPCMSample instrument, Point pos);
        public delegate void DPCMSampleDelegate(DPCMSample sample);

        public event InstrumentDelegate InstrumentColorChanged;
        public event InstrumentDelegate InstrumentReplaced;
        public event InstrumentDelegate InstrumentDeleted;
        public event InstrumentPointDelegate InstrumentDroppedOutside;
        public event SongDelegate SongModified;
        public event ArpeggioDelegate ArpeggioColorChanged;
        public event ArpeggioDelegate ArpeggioDeleted;
        public event ArpeggioPointDelegate ArpeggioDroppedOutside;
        public event DPCMSampleDelegate DPCMSampleReloaded;
        public event DPCMSampleDelegate DPCMSampleColorChanged;
        public event DPCMSampleDelegate DPCMSampleDeleted;
        public event DPCMSamplePointDelegate DPCMSampleDraggedOutside;
        public event DPCMSamplePointDelegate DPCMSampleMapped;
        public event EmptyDelegate ProjectModified;

        public ProjectExplorer()
        {
            UpdateRenderCoords();

            registerValues = new NesApu.NesRegisterValues();
            registerViewers[ExpansionType.None] = new ApuRegisterViewer(registerValues);
            registerViewers[ExpansionType.Vrc6] = new Vrc6RegisterViewer(registerValues);
            registerViewers[ExpansionType.Vrc7] = new Vrc7RegisterViewer(registerValues);
            registerViewers[ExpansionType.Fds]  = new FdsRegisterViewer(registerValues);
            registerViewers[ExpansionType.Mmc5] = new Mmc5RegisterViewer(registerValues);
            registerViewers[ExpansionType.N163] = new N163RegisterViewer(registerValues);
            registerViewers[ExpansionType.S5B]  = new S5BRegisterViewer(registerValues);
            registerViewers[ExpansionType.EPSM] = new EpsmRegisterViewer(registerValues);
        }

        private void UpdateRenderCoords(bool updateVirtualSizeY = true)
        {
            expandButtonSizeX    = ScaleForMainWindow(DefaultExpandButtonSizeX);
            buttonIconPosX       = ScaleForMainWindow(DefaultButtonIconPosX);      
            buttonIconPosY       = ScaleForMainWindow(DefaultButtonIconPosY);      
            buttonTextPosX       = ScaleForMainWindow(DefaultButtonTextPosX);      
            buttonTextNoIconPosX = ScaleForMainWindow(DefaultButtonTextNoIconPosX);
            expandButtonPosX     = ScaleForMainWindow(DefaultExpandButtonPosX);
            expandButtonPosY     = ScaleForMainWindow(DefaultExpandButtonPosY);
            subButtonSpacingX    = ScaleForMainWindow(DefaultSubButtonSpacingX);   
            subButtonPosY        = ScaleForMainWindow(DefaultSubButtonPosY);       
            buttonSizeY          = ScaleForMainWindow(DefaultButtonSizeY);
            sliderPosX           = ScaleForMainWindow(DefaultSliderPosX);
            sliderPosY           = ScaleForMainWindow(DefaultSliderPosY);
            sliderSizeX          = ScaleForMainWindow(DefaultSliderSizeX);
            sliderSizeY          = ScaleForMainWindow(DefaultSliderSizeY);
            checkBoxPosX         = ScaleForMainWindow(DefaultCheckBoxPosX);
            checkBoxPosY         = ScaleForMainWindow(DefaultCheckBoxPosY);
            paramRightPadX       = ScaleForMainWindow(DefaultParamRightPadX);
            draggedLineSizeY     = ScaleForMainWindow(DefaultDraggedLineSizeY);
            registerLabelSizeX   = ScaleForMainWindow(DefaultRegisterLabelSizeX);
            topTabSizeY          = Settings.ShowRegisterViewer ? buttonSizeY : 0;
            scrollAreaSizeY      = Height - topTabSizeY;
            contentSizeX         = Width;

            if (updateVirtualSizeY)
            {
                if (App != null && App.Project != null)
                {
                    virtualSizeY = 0;
                    foreach (var btn in buttons)
                        virtualSizeY += btn.height;
                }
                else
                {
                    virtualSizeY = Height;
                }

                needsScrollBar = virtualSizeY > scrollAreaSizeY;

                if (needsScrollBar)
                    scrollBarThickness = ScaleForMainWindow(Settings.ScrollBars == 1 ? DefaultScrollBarThickness1 : (Settings.ScrollBars == 2 ? DefaultScrollBarThickness2 : 0));
                else
                    scrollBarThickness = 0;

                contentSizeX = Width - scrollBarThickness;
            }
        }

        public void Reset()
        {
            scrollY = 0;
            expandedInstrument = null;
            expandedSample = null;
            selectedInstrumentTab = null;
            RefreshButtons();
            MarkDirty();
        }

        public void LayoutChanged()
        {
            UpdateRenderCoords();
            ClampScroll();
            MarkDirty();
        }

        private ButtonType GetButtonTypeForParam(ParamInfo param)
        {
            var widgetType = ButtonType.ParamSlider;

            if (param.CustomDraw != null)
                widgetType = ButtonType.ParamCustomDraw;
            else if (param.IsList)
                widgetType = ButtonType.ParamList;
            else if (param.MaxValue == 1)
                widgetType = ButtonType.ParamCheckbox;

            return widgetType;
        }

        private int GetHeightForRegisterRows(RegisterViewerRow[] regs)
        {
            var h = 0;
            for (int i = 0; i < regs.Length; i++)
                h += ScaleForMainWindow(regs[i].Height);
            return h;
        }

        public void RefreshButtons(bool invalidate = true)
        {
            Debug.Assert(captureOperation != CaptureOperation.MoveSlider);

            if (selectedTab == TabType.Registers && !Settings.ShowRegisterViewer)
                selectedTab = TabType.Project;

            UpdateRenderCoords(false);

            buttons.Clear();
            var project = App.Project;

            if (!IsRenderInitialized || project == null)
                return;

            if (selectedTab == TabType.Project)
            {
	            var projectText = string.IsNullOrEmpty(project.Author) ? $"{project.Name}" : $"{project.Name} ({project.Author})";

	            buttons.Add(new Button(this) { type = ButtonType.ProjectSettings, text = projectText });
	            buttons.Add(new Button(this) { type = ButtonType.SongHeader, text = "Songs" });

	            foreach (var song in project.Songs)
	                buttons.Add(new Button(this) { type = ButtonType.Song, song = song, text = song.Name, color = song.Color, atlas = bmpMiscAtlas, atlasIdx = (int)MiscImageIndices.Song, textBrush = ThemeResources.BlackBrush });

	            buttons.Add(new Button(this) { type = ButtonType.InstrumentHeader, text = "Instruments" });
	            buttons.Add(new Button(this) { type = ButtonType.Instrument, color = Theme.LightGreyFillColor1, textBrush = ThemeResources.BlackBrush, atlas = bmpExpansionsAtlas, atlasIdx = ExpansionType.None });

	            foreach (var instrument in project.Instruments)
	            {
	                buttons.Add(new Button(this) { type = ButtonType.Instrument, instrument = instrument, text = instrument.Name, color = instrument.Color, textBrush = ThemeResources.BlackBrush, atlas = bmpExpansionsAtlas, atlasIdx = instrument.Expansion });

	                if (instrument != null && instrument == expandedInstrument)
	                {
	                    var instrumentParams = InstrumentParamProvider.GetParams(instrument);

	                    if (instrumentParams != null)
	                    {
	                        List<string> tabNames = null;

	                        foreach (var param in instrumentParams)
	                        {
	                            if (param.HasTab)
	                            {
	                                if (tabNames == null)
	                                    tabNames = new List<string>();

	                                if (!tabNames.Contains(param.TabName))
	                                    tabNames.Add(param.TabName);
	                            }
	                        }

	                        var tabCreated = false;

	                        foreach (var param in instrumentParams)
	                        {
	                            if (!tabCreated && param.HasTab)
	                            {
	                                buttons.Add(new Button(this) { type = ButtonType.ParamTabs, param = param, color = instrument.Color, tabNames = tabNames.ToArray() });
	                                tabCreated = true;
	                            }

	                            if (param.HasTab)
	                            {
	                                if (string.IsNullOrEmpty(selectedInstrumentTab) || selectedInstrumentTab == param.TabName)
	                                {
	                                    selectedInstrumentTab = param.TabName;
	                                }

	                                if (param.TabName != selectedInstrumentTab)
	                                {
	                                    continue;
	                                }
	                            }

	                            var sizeY = param.CustomHeight > 0 ? param.CustomHeight * buttonSizeY : buttonSizeY;
	                            buttons.Add(new Button(this) { type = GetButtonTypeForParam(param), param = param, instrument = instrument, color = instrument.Color, text = param.Name, textBrush = ThemeResources.BlackBrush, paramScope = TransactionScope.Instrument, paramObjectId = instrument.Id, height = sizeY });
	                        }
	                    }
	                }
	            }

	            buttons.Add(new Button(this) { type = ButtonType.DpcmHeader });
	            foreach (var sample in project.Samples)
	            {
	                buttons.Add(new Button(this) { type = ButtonType.Dpcm, sample = sample, color = sample.Color, textBrush = ThemeResources.BlackBrush, atlas = bmpMiscAtlas, atlasIdx = (int)MiscImageIndices.DPCM });

	                if (sample == expandedSample)
	                {
	                    var sampleParams = DPCMSampleParamProvider.GetParams(sample);

	                    foreach (var param in sampleParams)
	                    {
	                        buttons.Add(new Button(this) { type = GetButtonTypeForParam(param), param = param, sample = sample, color = sample.Color, text = param.Name, textBrush = ThemeResources.BlackBrush, paramScope = TransactionScope.DPCMSample, paramObjectId = sample.Id });
	                    }
	                }
	            }

	            buttons.Add(new Button(this) { type = ButtonType.ArpeggioHeader, text = "Arpeggios" });
	            buttons.Add(new Button(this) { type = ButtonType.Arpeggio, text = "None", color = Theme.LightGreyFillColor1, textBrush = ThemeResources.BlackBrush });

	            foreach (var arpeggio in project.Arpeggios)
	            {
	                buttons.Add(new Button(this) { type = ButtonType.Arpeggio, arpeggio = arpeggio, text = arpeggio.Name, color = arpeggio.Color, textBrush = ThemeResources.BlackBrush, atlas = bmpEnvelopesAtlas, atlasIdx = EnvelopeType.Arpeggio });
	            }
            }
            else
            {
                var expansions = project.GetActiveExpansions();
                foreach (var e in expansions)
                {
                    var expRegs = registerViewers[e];

                    if (expRegs != null)
                    {
                        var expName = e == ExpansionType.None ? "2A03" : ExpansionType.Names[e];
                        buttons.Add(new Button(this) { type = ButtonType.RegisterExpansionHeader, text = $"{expName} Registers", atlas = bmpExpansionsAtlas, atlasIdx = e, imageTint = Theme.LightGreyFillColor2 });
                        buttons.Add(new Button(this) { type = ButtonType.ExpansionRegistersFirst + e, height = GetHeightForRegisterRows(expRegs.ExpansionRows), regs = expRegs.ExpansionRows, gradient = false });

                        var channels = Channel.GetChannelsForExpansionMask(ExpansionType.GetMaskFromValue(e), project.ExpansionNumN163Channels);
                        var firstChannel = e == ExpansionType.None ? 0 : ChannelType.ExpansionAudioStart;
                        for (int i = firstChannel; i < channels.Length; i++)
                        {
                            var c = channels[i];
                            var idx = channels[i] - channels[firstChannel]; // Assumes contiguous channels.
                            var chanRegs = expRegs.ChannelRows[idx];

                            if (chanRegs != null && chanRegs.Length > 0)
                            {
                                buttons.Add(new Button(this) { type = ButtonType.RegisterChannelHeader, text = ChannelType.Names[c], atlas = bmpAtlasChannels, atlasIdx = c, imageTint = Theme.LightGreyFillColor2 });
                                buttons.Add(new Button(this) { type = ButtonType.ChannelStateFirst + c, height = GetHeightForRegisterRows(chanRegs), regs = chanRegs, gradient = false });
                            }
                        }
                    }
                }
            }
			
            flingVelY = 0.0f;
            highlightedButtonIdx = -1;

            UpdateRenderCoords();
            ClampScroll();

            if (invalidate)
                MarkDirty();
        }

        protected override void OnRenderInitialized(RenderGraphics g)
        {
            Debug.Assert(MiscImageNames.Length      == (int)MiscImageIndices.Count);
            Debug.Assert(EnvelopesImageNames.Length == (int)EnvelopesImageIndices.Count);

            sliderFillBrush = g.CreateSolidBrush(Color.FromArgb(64, Color.Black));
            disabledBrush = g.CreateSolidBrush(Color.FromArgb(64, Color.Black));
            bmpMiscAtlas = g.CreateBitmapAtlasFromResources(MiscImageNames);
            bmpExpansionsAtlas = g.CreateBitmapAtlasFromResources(ExpansionType.Icons);
            bmpEnvelopesAtlas = g.CreateBitmapAtlasFromResources(EnvelopesImageNames);
            bmpAtlasChannels = g.CreateBitmapAtlasFromResources(ChannelType.Icons);

            var color0 = Theme.LightGreyFillColor2; // Grey
            var color1 = Theme.CustomColors[14, 5]; // Orange
            var color2 = Theme.CustomColors[0,  5]; // Red

            for (int i = 0; i < registerBrushes.Length; i++)
            {
                var alpha = i / (float)(registerBrushes.Length - 1);
                var color = Color.FromArgb(
                    (int)Utils.Lerp(color2.R, color0.R, alpha),
                    (int)Utils.Lerp(color2.G, color0.G, alpha),
                    (int)Utils.Lerp(color2.B, color0.B, alpha));
                registerBrushes[i] = g.CreateSolidBrush(color);


                /*
                if (alpha < 0.5f)
                {
                    alpha *= 2.0f;
                    var color = Color.FromArgb(
                        (int)Utils.Lerp(color2.R, color1.R, alpha),
                        (int)Utils.Lerp(color2.G, color1.G, alpha),
                        (int)Utils.Lerp(color2.B, color1.B, alpha));
                    registerBrushes[i] = g.CreateSolidBrush(color);

                }
                else
                {
                    alpha = (alpha - 0.5f) * 2.0f;
                    var color = Color.FromArgb(
                        (int)Utils.Lerp(color1.R, color0.R, alpha),
                        (int)Utils.Lerp(color1.G, color0.G, alpha),
                        (int)Utils.Lerp(color1.B, color0.B, alpha));
                    registerBrushes[i] = g.CreateSolidBrush(color);
                }
                */
            }

            if (PlatformUtils.IsMobile)
                bitmapScale = g.WindowScaling * 0.25f;

            RefreshButtons();
        }

        protected override void OnRenderTerminated()
        {
            Utils.DisposeAndNullify(ref sliderFillBrush);
            Utils.DisposeAndNullify(ref disabledBrush);
            Utils.DisposeAndNullify(ref bmpMiscAtlas);
            Utils.DisposeAndNullify(ref bmpExpansionsAtlas);
            Utils.DisposeAndNullify(ref bmpEnvelopesAtlas);
            Utils.DisposeAndNullify(ref bmpAtlasChannels);

            for (int i = 0; i < registerBrushes.Length; i++)
                Utils.DisposeAndNullify(ref registerBrushes[i]);
        }

        protected bool ShowExpandButtons()
        {
            if (App.Project != null)
            {
                if (App.Project.Instruments.Find(i => InstrumentParamProvider.HasParams(i)) != null)
                    return true;

                if (App.Project.Samples.Count > 0)
                    return true;
            }

            return false;
        }

        private void RenderDebug(RenderGraphics g)
        {
#if DEBUG
            if (PlatformUtils.IsMobile)
            {
                var c = g.CreateCommandList();
                c.FillRectangle(mouseLastX - 30, mouseLastY - 30, mouseLastX + 30, mouseLastY + 30, ThemeResources.WhiteBrush);
                g.DrawCommandList(c);
            }
#endif
        }

        private void RenderTabs(RenderCommandList c)
        {
            var numTabs = (int)TabType.Count;
            var tabSizeX = Width / numTabs;

            for (int i = 0; i < numTabs; i++)
            {
                var activeTab = i == (int)selectedTab;
                var tabColor  = activeTab ? Theme.DarkGreyFillColor1 : Theme.Darken(Theme.DarkGreyFillColor1);
                var textBrush = activeTab ? ThemeResources.LightGreyFillBrush2 : ThemeResources.LightGreyFillBrush1;
                var textFont  = activeTab ? ThemeResources.FontMediumBold : ThemeResources.FontMedium;
                var x0 = (i + 0) * tabSizeX;
                var x1 = (i + 1) * tabSizeX;
                c.FillAndDrawRectangle(x0, 0, x1, buttonSizeY, c.Graphics.GetVerticalGradientBrush(tabColor, buttonSizeY, 0.8f), ThemeResources.BlackBrush, 1);
                c.DrawText(TabNames[i], textFont, x0, 0, textBrush, RenderTextFlags.MiddleCenter, tabSizeX, buttonSizeY);
            }
        }

        private void RenderRegisterRows(NesApu.NesRegisterValues regValues, RenderCommandList c, Button button, int exp = -1)
        {
            int y = 0;

            for (int i = 0; i < button.regs.Length; i++)
            {
                var reg = button.regs[i];
                var regSizeY = ScaleForMainWindow(reg.Height);

                c.PushTranslation(0, y);

                if (i != 0)
                    c.DrawLine(0, -1, contentSizeX, -1, ThemeResources.BlackBrush);

                if (reg.CustomDraw != null)
                {
                    var label = reg.Label;
                    c.DrawText(label, ThemeResources.FontSmall, buttonTextNoIconPosX, 0, ThemeResources.LightGreyFillBrush2, RenderTextFlags.Middle, 0, regSizeY);

                    c.PushTranslation(registerLabelSizeX + 1, 0);
                    reg.CustomDraw(c, ThemeResources, new Rectangle(0, 0, contentSizeX - registerLabelSizeX - 1, regSizeY));
                    c.PopTransform();
                }
                else if (reg.GetValue != null)
                {
                    var label = reg.Label;
                    var value = reg.GetValue().ToString();
                    var flags = reg.Monospace ? RenderTextFlags.Middle | RenderTextFlags.Monospace : RenderTextFlags.Middle;

                    c.DrawText(label, ThemeResources.FontSmall, buttonTextNoIconPosX, 0, ThemeResources.LightGreyFillBrush2, RenderTextFlags.Middle, 0, regSizeY);
                    c.DrawText(value, ThemeResources.FontSmall, buttonTextNoIconPosX + registerLabelSizeX, 0, ThemeResources.LightGreyFillBrush2, flags, 0, regSizeY);
                }
                else
                {
                    Debug.Assert(exp >= 0);

                    c.DrawText(reg.Label, ThemeResources.FontSmall, buttonTextNoIconPosX, 0, ThemeResources.LightGreyFillBrush2, RenderTextFlags.Middle, 0, regSizeY);

                    var flags = RenderTextFlags.Monospace | RenderTextFlags.Middle;
                    var x = buttonTextNoIconPosX + registerLabelSizeX;

                    for (var r = reg.AddStart; r <= reg.AddEnd; r++)
                    {
                        for (var s = reg.SubStart; s <= reg.SubEnd; s++)
                        {
                            var val = regValues.GetRegisterValue(exp, r, out var age, s);
                            var str = $"${val:X2} ";
                            var brush = registerBrushes[Math.Min(age, registerBrushes.Length - 1)];

                            c.DrawText(str, ThemeResources.FontSmall, x, 0, brush, flags, 0, regSizeY);
                            x += (int)c.Graphics.MeasureString(str, ThemeResources.FontSmall, true);
                        }
                    }
                }

                c.PopTransform();
                y += regSizeY;
            }

            c.DrawLine(registerLabelSizeX, 0, registerLabelSizeX, button.height, ThemeResources.BlackBrush);
        }

        protected override void OnRender(RenderGraphics g)
        {
            RenderCommandList ct = null;
            RenderCommandList c = null;

            if (Settings.ShowRegisterViewer)
            {
                ct = g.CreateCommandList();
                RenderTabs(ct);
                c = g.CreateCommandList();
                c.PushTranslation(0, buttonSizeY);
            }
            else
            {
                c = g.CreateCommandList();
            }

            if (selectedTab == TabType.Registers)
            {
                App.ActivePlayer.GetRegisterValues(registerValues);
            }

            c.DrawLine(0, 0, 0, Height, ThemeResources.BlackBrush);

            var showExpandButton = ShowExpandButtons();
            var firstParam = true;
            var y = -scrollY;
            var iconSize = ScaleCustom(bmpEnvelopesAtlas.GetElementSize(0).Width, bitmapScale);

            var minInstIdx = 1000000;
            var maxInstIdx = 0;
            var minArpIdx  = 1000000;
            var maxArpIdx  = 0;

            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                var atlas = button.atlas;
                var atlasIdx = button.atlasIdx;
                var highlighted = i == highlightedButtonIdx;

                if (y + button.height >= 0)
                {
                    c.PushTranslation(0, y);

                    var groupSizeY = button.height;
                    var drawBackground = true;

                    if (button.IsParam)
                    {
                        if (firstParam)
                        {
                            for (int j = i + 1; j < buttons.Count; j++)
                            {
                                if (!buttons[j].IsParam)
                                    break;

                                groupSizeY += buttons[j].height;
                            }

                            firstParam = false;
                        }
                        else
                        {
                            drawBackground = false;
                        }
                    }

                    if (drawBackground)
                    {
                        c.FillAndDrawRectangle(0, 0, contentSizeX, groupSizeY, button.gradient ? g.GetVerticalGradientBrush(button.color, groupSizeY, 0.8f) : g.GetSolidBrush(button.color), ThemeResources.BlackBrush, 1);
                    }

                    if (button.type == ButtonType.Instrument)
                    {
                        if (button.instrument != null)
                        {
                            minInstIdx = Math.Min(minInstIdx, i);
                            maxInstIdx = Math.Max(maxInstIdx, i);
                        }
                    }
                    else if (button.type == ButtonType.Arpeggio)
                    {
                        if (button.arpeggio != null)
                        {
                            minArpIdx = Math.Min(minArpIdx, i);
                            maxArpIdx = Math.Max(maxArpIdx, i);
                        }
                    }

                    var leftPadding = 0;
                    var leftAligned = button.type == ButtonType.Instrument || button.type == ButtonType.Song || button.type == ButtonType.ParamSlider || button.type == ButtonType.ParamCheckbox || button.type == ButtonType.ParamList || button.type == ButtonType.Arpeggio || button.type == ButtonType.Dpcm || button.type == ButtonType.ParamCustomDraw || button.type == ButtonType.ParamTabs;

                    if (showExpandButton && leftAligned)
                    {
                        leftPadding = 1 + expandButtonSizeX;
                        c.PushTranslation(leftPadding, 0);
                    }

                    var enabled = button.param == null || button.param.IsEnabled == null || button.param.IsEnabled();
                    var ellipsisFlag = button.TextEllipsis ? RenderTextFlags.Ellipsis : RenderTextFlags.None;
                    var player = App.ActivePlayer;

                    if (button.type == ButtonType.ParamCustomDraw)
                    {
                        button.param.CustomDraw(c, ThemeResources, new Rectangle(0, 0, contentSizeX - leftPadding - paramRightPadX - 1, button.height), button.param.CustomUserData1, button.param.CustomUserData2);
                    }
                    else if (button.type >= ButtonType.ExpansionRegistersFirst && button.type < ButtonType.ChannelStateFirst)
                    {
                        RenderRegisterRows(registerValues, c, button, button.type - ButtonType.ExpansionRegistersFirst);
                    }
                    else if (button.type >= ButtonType.ChannelStateFirst)
                    {
                        RenderRegisterRows(registerValues, c, button);
                    }
                    else
                    {
                        if (button.Text != null)
                        {
                            c.DrawText(button.Text, button.Font, atlas == null ? buttonTextNoIconPosX : buttonTextPosX, 0, enabled ? button.textBrush : disabledBrush, button.TextAlignment | ellipsisFlag | RenderTextFlags.Middle, contentSizeX - buttonTextPosX, buttonSizeY);
                        }

                        if (atlas != null)
                        {
                            c.DrawBitmapAtlas(atlas, atlasIdx, buttonIconPosX, buttonIconPosY, 1.0f, bitmapScale, button.imageTint);
                            if (highlighted)
                                c.DrawRectangle(buttonIconPosX, buttonIconPosY, buttonIconPosX + iconSize - 4, buttonIconPosY + iconSize - 4, ThemeResources.WhiteBrush, 2, true);
                        }
                    }

                    if (leftPadding != 0)
                        c.PopTransform();

                    if (button.param != null)
                    {
                        var paramVal = button.param.GetValue();
                        var paramStr = button.param.GetValueString();

                        if (button.type == ButtonType.ParamSlider)
                        {
                            var valSizeX = (int)Math.Round((paramVal - button.param.MinValue) / (float)(button.param.MaxValue - button.param.MinValue) * sliderSizeX);

                            c.PushTranslation(contentSizeX - sliderPosX, sliderPosY);
                            c.FillRectangle(0, 0, valSizeX, sliderSizeY, sliderFillBrush);
                            c.DrawRectangle(0, 0, sliderSizeX, sliderSizeY, enabled ? ThemeResources.BlackBrush : disabledBrush, 1);
                            c.DrawText(paramStr, ThemeResources.FontMedium, 0, -sliderPosY, ThemeResources.BlackBrush, RenderTextFlags.MiddleCenter, sliderSizeX, buttonSizeY);
                            c.PopTransform();
                        }
                        else if (button.type == ButtonType.ParamCheckbox)
                        {
                            c.DrawBitmapAtlas(bmpMiscAtlas, paramVal == 0 ? (int)MiscImageIndices.CheckBoxNo : (int)MiscImageIndices.CheckBoxYes, contentSizeX - checkBoxPosX, checkBoxPosY, enabled ? 1.0f : 0.25f, bitmapScale, Color.Black);
                        }
                        else if (button.type == ButtonType.ParamList)
                        {
                            var paramPrev = button.param.SnapAndClampValue(paramVal - 1);
                            var paramNext = button.param.SnapAndClampValue(paramVal + 1);
                            var buttonWidth = ScaleCustom(bmpMiscAtlas.GetElementSize((int)MiscImageIndices.ButtonLeft).Width, bitmapScale);

                            c.PushTranslation(contentSizeX - sliderPosX, sliderPosY);
                            c.DrawBitmapAtlas(bmpMiscAtlas, (int)MiscImageIndices.ButtonLeft, 0, 0, paramVal == paramPrev || !enabled ? 0.25f : 1.0f, bitmapScale, Color.Black);
                            c.DrawBitmapAtlas(bmpMiscAtlas, (int)MiscImageIndices.ButtonRight, sliderSizeX - buttonWidth, 0, paramVal == paramNext || !enabled ? 0.25f : 1.0f, bitmapScale, Color.Black);
                            c.DrawText(paramStr, ThemeResources.FontMedium, 0, -sliderPosY, ThemeResources.BlackBrush, RenderTextFlags.MiddleCenter, sliderSizeX, button.height);
                            c.PopTransform();
                        }
                        else if (button.type == ButtonType.ParamTabs)
                        {
                            var tabWidth = Utils.DivideAndRoundUp(contentSizeX - leftPadding - paramRightPadX, button.tabNames.Length);

                            for (var j = 0; j < button.tabNames.Length; j++)
                            {
                                var tabName      = button.tabNames[j];
                                var tabSelect    = tabName == selectedInstrumentTab;
                                var tabFont      = tabSelect ? ThemeResources.FontMediumBold : ThemeResources.FontMedium;
                                var tabLineBrush = tabSelect ? ThemeResources.BlackBrush : g.GetSolidBrush(Color.Black, 1.0f, 0.5f);
                                var tabLine      = tabSelect ? 3 : 1;

                                c.PushTranslation(leftPadding + tabWidth * j, 0);
                                c.DrawText(tabName, tabFont, 0, 0, tabLineBrush, RenderTextFlags.MiddleCenter, tabWidth, button.height);
                                c.DrawLine(0, button.height - tabLine / 2, tabWidth, button.height - tabLine / 2, tabLineBrush, ScaleLineForMainWindow(tabLine));
                                c.PopTransform();

                            }
                        }
                    }
                    else
                    {
                        var subButtons = button.GetSubButtons(out var activeMask);
                        var tint = button.SubButtonTint;

                        if (subButtons != null)
                        {
                            for (int j = 0, x = contentSizeX - subButtonSpacingX; j < subButtons.Length; j++, x -= subButtonSpacingX)
                            {
                                atlas = button.GetIcon(subButtons[j], out atlasIdx);

                                if (subButtons[j] == SubButtonType.Expand)
                                {
                                    c.DrawBitmapAtlas(atlas, atlasIdx, expandButtonPosX, expandButtonPosY, 1.0f, bitmapScale, tint);
                                }
                                else
                                {
                                    c.DrawBitmapAtlas(atlas, atlasIdx, x, subButtonPosY, (activeMask & (1 << j)) != 0 ? 1.0f : 0.2f, bitmapScale, tint);

                                    if (highlighted && subButtons[j] < SubButtonType.EnvelopeMax)
                                        c.DrawRectangle(x, subButtonPosY, x + iconSize - 4, subButtonPosY + iconSize - 4, ThemeResources.WhiteBrush, 2, true);
                                }
                            }
                        }
                    }

                    c.PopTransform();
                }

                y += button.height;

                if (y > scrollAreaSizeY)
                {
                    break;
                }
            }

            if (captureOperation != CaptureOperation.None && captureThresholdMet)
            {
                if (captureOperation == CaptureOperation.DragSong)
                {
                    var pt = PlatformUtils.IsDesktop ? PointToClient(Cursor.Position) : new Point(mouseLastX, mouseLastY);
                    var buttonIdx = GetButtonAtCoord(pt.X, pt.Y - buttonSizeY / 2, out _);

                    if (buttonIdx >= 0)
                    {
                        var button = buttons[buttonIdx];

                        if (button.type == ButtonType.Song ||
                            button.type == ButtonType.SongHeader)
                        {
                            var lineY = (buttonIdx + 1) * buttonSizeY - scrollY;
                            c.DrawLine(0, lineY, contentSizeX, lineY, c.Graphics.GetSolidBrush(draggedSong.Color), draggedLineSizeY);
                        }
                    }
                }
                else if (captureOperation == CaptureOperation.DragInstrument || (captureOperation == CaptureOperation.DragArpeggio && draggedArpeggio != null))
                {
                    var pt = PlatformUtils.IsDesktop ? PointToClient(Cursor.Position) : new Point(mouseLastX, mouseLastY);
                    if (ClientRectangle.Contains(pt))
                    {
                        if (envelopeDragIdx >= 0 || PlatformUtils.IsMobile)
                        {
                            var button = buttons[captureButtonIdx];
                            var bx = pt.X - captureButtonRelX;
                            var by = pt.Y - captureButtonRelY - topTabSizeY;

                            if (envelopeDragIdx >= 0)
                                c.DrawBitmapAtlas(bmpEnvelopesAtlas, envelopeDragIdx, bx, by, 0.5f, bitmapScale, Color.Black);
                            else
                                c.DrawBitmapAtlas(button.atlas, button.atlasIdx, bx, by, 0.5f, bitmapScale, Color.Black);

                            if (PlatformUtils.IsMobile)
                                c.DrawRectangle(bx, by, bx + iconSize - 4, by + iconSize - 4, ThemeResources.WhiteBrush, 2, true);
                        }
                        else
                        {
                            var minY = (captureOperation == CaptureOperation.DragInstrument ? minInstIdx : minArpIdx) * buttonSizeY - scrollY + topTabSizeY;
                            var maxY = (captureOperation == CaptureOperation.DragInstrument ? maxInstIdx : maxArpIdx) * buttonSizeY - scrollY + topTabSizeY;
                            var color = (captureOperation == CaptureOperation.DragInstrument ? draggedInstrument.Color : draggedArpeggio.Color);
                            var dragY = Utils.Clamp(pt.Y - captureButtonRelY, minY, maxY) - topTabSizeY;

                            c.FillRectangle(0, dragY, contentSizeX, dragY + buttonSizeY, c.Graphics.GetSolidBrush(color, 1, 0.5f));
                        }
                    }
                }
            }

            if (needsScrollBar)
            {
                int scrollBarSizeY = (int)Math.Round(scrollAreaSizeY * (scrollAreaSizeY / (float)virtualSizeY));
                int scrollBarPosY = (int)Math.Round(scrollAreaSizeY * (scrollY / (float)virtualSizeY));

                c.FillAndDrawRectangle(contentSizeX, 0, Width - 1, Height, ThemeResources.DarkGreyFillBrush1, ThemeResources.BlackBrush);
                c.FillAndDrawRectangle(contentSizeX, scrollBarPosY, Width - 1, scrollBarPosY + scrollBarSizeY, ThemeResources.MediumGreyFillBrush1, ThemeResources.BlackBrush);
            }

            c.DrawLine(0, 0, Width, 0, ThemeResources.BlackBrush);

            g.Clear(Theme.DarkGreyFillColor1);

            if (Settings.ShowRegisterViewer)
            {
                c.PopTransform();
                g.DrawCommandList(ct);
                g.DrawCommandList(c, new Rectangle(0, buttonSizeY, Width, Height));
            }
            else
            {
                g.DrawCommandList(c);
            }

            RenderDebug(g);
        }

        private bool GetScrollBarParams(out int posY, out int sizeY)
        {
            if (scrollBarThickness > 0)
            {
                sizeY = (int)Math.Round(scrollAreaSizeY * (scrollAreaSizeY / (float)virtualSizeY));
                posY  = (int)Math.Round(scrollAreaSizeY * (scrollY         / (float)virtualSizeY));
                return true;
            }
            else
            {
                posY = 0;
                sizeY = 0;
                return false;
            }
        }

        private bool ClampScroll()
        {
            int minScrollY = 0;
            int maxScrollY = Math.Max(virtualSizeY - scrollAreaSizeY, 0);

            var scrolled = true;
            if (scrollY < minScrollY) { scrollY = minScrollY; scrolled = false; }
            if (scrollY > maxScrollY) { scrollY = maxScrollY; scrolled = false; }
            return scrolled;
        }

        private bool DoScroll(int deltaY)
        {
            scrollY -= deltaY;
            MarkDirty();
            return ClampScroll();
        }

        protected void UpdateCursor()
        {
            if (captureOperation == CaptureOperation.DragInstrument && captureThresholdMet)
            {
                Cursor.Current = envelopeDragIdx == -1 ? Cursors.DragCursor : Cursors.CopyCursor;
            }
            else if ((captureOperation == CaptureOperation.DragArpeggio || captureOperation == CaptureOperation.DragSample) && captureThresholdMet)
            {
                Cursor.Current = Cursors.DragCursor;
            }
            else
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private int GetButtonAtCoord(int x, int y, out SubButtonType sub, out int buttonRelX, out int buttonRelY)
        {
            sub = SubButtonType.Max;
            buttonRelX = 0;
            buttonRelY = 0;

            if (needsScrollBar && x >= contentSizeX)
                return -1;

            var absY = y + scrollY;
            var buttonIndex = -1;
            var buttonBaseY = topTabSizeY;

            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                if (absY >= buttonBaseY && absY < buttonBaseY + button.height)
                {
                    buttonIndex = i;
                    break;
                }
                buttonBaseY += button.height;
            }

            if (buttonIndex >= 0 && buttonIndex < buttons.Count)
            {
                var button = buttons[buttonIndex];

                if (ShowExpandButtons() && x < (expandButtonPosX + expandButtonSizeX) && ((button.instrument != null && InstrumentParamProvider.HasParams(button.instrument)) || (button.sample != null))) 
                {
                    sub = SubButtonType.Expand;
                    return buttonIndex;
                }

                buttonRelX = x;
                buttonRelY = y - buttonBaseY + scrollY;

                var subButtons = button.GetSubButtons(out _);
                if (subButtons != null)
                {
                    y -= (buttonBaseY - scrollY);

                    for (int i = 0; i < subButtons.Length; i++)
                    {
                        if (subButtons[i] == SubButtonType.Expand)
                            continue;

                        int sx = contentSizeX - subButtonSpacingX * (i + 1);
                        int sy = subButtonPosY;
                        int dx = x - sx;
                        int dy = y - sy;

                        if (dx >= 0 && dx < 16 * DpiScaling.MainWindow &&
                            dy >= 0 && dy < 16 * DpiScaling.MainWindow)
                        {
                            buttonRelX = dx;
                            buttonRelY = dy;
                            sub = subButtons[i];
                            break;
                        }
                    }
                }

                return buttonIndex;
            }
            else
            {
                return -1;
            }
        }

        private int GetButtonAtCoord(int x, int y, out SubButtonType sub)
        {
            return GetButtonAtCoord(x, y, out sub, out _, out _);
        }

        private void UpdateToolTip(int x, int y)
        {
            var redTooltip = false;
            var tooltip = "";
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType);

            // TODO: Store this in the button itself... this is stupid.
            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];
                var buttonType = button.type;

                if (buttonType == ButtonType.SongHeader)
                {
                    if (subButtonType == SubButtonType.Add)
                    {
                        tooltip = "{MouseLeft} Add new song";
                    }
                    else if (subButtonType == SubButtonType.Load)
                    {
                        tooltip = "{MouseLeft} Import/merge song from another project";
                    }
                }
                else if (buttonType == ButtonType.Song)
                {
                    if (subButtonType == SubButtonType.Properties)
                    {
                        tooltip = "{MouseLeft} Song/Tempo properties";
                    }
                    else
                    {
                        tooltip = "{MouseLeft} Make song current - {MouseLeft}{MouseLeft} Song/Tempo properties - {MouseRight} Delete song\n{MouseLeft} {Drag} Re-order song";
                    }
                }
                else if (buttonType == ButtonType.InstrumentHeader)
                {
                    if (subButtonType == SubButtonType.Add)
                    {
                        tooltip = "{MouseLeft} Add new instrument";
                    }
                    else if (subButtonType == SubButtonType.Load)
                    {
                        tooltip = "{MouseLeft} Import/merge instrument from another project";
                    }
                }
                else if (buttonType == ButtonType.ArpeggioHeader)
                {
                    if (subButtonType == SubButtonType.Add)
                    {
                        tooltip = "{MouseLeft} Add new arpeggio";
                    }
                }
                else if (buttonType == ButtonType.ProjectSettings)
                {
                    if (subButtonType == SubButtonType.Properties)
                    {
                        tooltip = "{MouseLeft} Project properties";
                    }
                    else
                    {
                        tooltip = "{MouseLeft}{MouseLeft} Project properties";
                    }
                }
                else if (buttonType == ButtonType.ParamCheckbox)
                {
                    if (x >= contentSizeX - checkBoxPosX)
                    {
                        tooltip = "{MouseLeft} Toggle value\n{MouseRight} Reset to default value";
                    }
                    else if (button.param.ToolTip != null)
                    {
                        tooltip = button.param.ToolTip;
                    }
                }
                else if (buttonType == ButtonType.ParamSlider)
                {
                    if (x >= contentSizeX - sliderPosX)
                    {
                        tooltip = "{MouseLeft} {Drag} Change value - {Shift} {MouseLeft} {Drag} Change value (fine)\n{MouseRight} Reset to default value";
                    }
                    else if (button.param.ToolTip != null)
                    {
                        tooltip = button.param.ToolTip;
                    }
                }
                else if (buttonType == ButtonType.ParamList)
                {
                    if (x >= contentSizeX - sliderPosX)
                    {
                        tooltip = "{MouseLeft} Change value\n{MouseRight} Reset to default value";
                    }
                    else if (button.param.ToolTip != null)
                    {
                        tooltip = button.param.ToolTip;
                    }
                }
                else if (buttonType == ButtonType.Instrument)
                {
                    if (subButtonType == SubButtonType.Max)
                    {
                        if (buttons[buttonIdx].instrument == null)
                            tooltip = "{MouseLeft} Select instrument";
                        else
                            tooltip = "{MouseLeft} Select instrument - {MouseLeft}{MouseLeft} Instrument properties\n{MouseRight} Delete instrument - {MouseLeft} {Drag} Replace instrument";
                    }
                    else
                    {
                        if (subButtonType == SubButtonType.DPCM)
                        {
                            tooltip = "{MouseLeft} Edit DPCM samples";
                        }
                        else if (subButtonType < SubButtonType.EnvelopeMax)
                        {
                            tooltip = $"{{MouseLeft}} Edit {EnvelopeType.Names[(int)subButtonType].ToLower()} envelope - {{MouseRight}} Delete envelope - {{MouseLeft}} {{Drag}} Copy envelope";
                        }
                        else if (subButtonType == SubButtonType.Overflow)
                        {
                            tooltip = "DPCM sample limit size limit is 16384 bytes. Some samples will not play correctly.";
                            redTooltip = true;
                        }
                        else if (subButtonType == SubButtonType.Properties)
                        {
                            tooltip = "{MouseLeft} Instrument properties";
                        }
                    }
                }
                else if (buttonType == ButtonType.Dpcm)
                {
                    if (subButtonType == SubButtonType.Play)
                    {
                        tooltip = "{MouseLeft} Preview processed DPCM sample\n{MouseRight} Play source sample";
                    }
                    else if (subButtonType == SubButtonType.EditWave)
                    {
                        tooltip = "{MouseLeft} Edit waveform";
                    }
                    else if (subButtonType == SubButtonType.Reload)
                    {
                        tooltip = "{MouseLeft} Reload source data (if available)\nOnly possible when data was loaded from a DMC/WAV file";
                    }
                    else if (subButtonType == SubButtonType.Save)
                    {
                        tooltip = "{MouseLeft} Export processed DMC file\n{MouseRight} Export source data (DMC or WAV)";
                    }
                    else if (subButtonType == SubButtonType.Max)
                    {
                        tooltip = "{MouseLeft}{MouseLeft} Edit properties\n{MouseRight} Delete sample";
                    }
                    else if (subButtonType == SubButtonType.Properties)
                    {
                        tooltip = "{MouseLeft} Instrument properties";
                    }
                }
                else if (buttonType == ButtonType.DpcmHeader)
                {
                    if (subButtonType == SubButtonType.Load)
                    {
                        tooltip = "{MouseLeft} Load DPCM sample from WAV or DMC file";
                    }
                }
                else if (buttonType == ButtonType.Arpeggio)
                {
                    if (subButtonType == SubButtonType.Max)
                    {
                        tooltip = "{MouseLeft} Select arpeggio - {MouseLeft}{MouseLeft} Arpeggio properties\n{MouseRight} Delete arpeggio - {MouseLeft} {Drag} Replace arpeggio";
                    }
                    else if (subButtonType == SubButtonType.Properties)
                    {
                        tooltip = "{MouseLeft} Arpeggio properties";
                    }
                }
            }
            else if (needsScrollBar && x > contentSizeX)
            {
                tooltip = "{MouseLeft} {Drag} Scroll";
            }

            App.SetToolTip(tooltip, redTooltip);
        }

        private void ScrollIfNearEdge(int x, int y)
        {
            int minY = PlatformUtils.IsMobile && IsLandscape ? 0      : -buttonSizeY;
            int maxY = PlatformUtils.IsMobile && IsLandscape ? Height : Height + buttonSizeY;

            scrollY += Utils.ComputeScrollAmount(y, minY, buttonSizeY, App.AverageTickRate * ScrollSpeedFactor, true);
            scrollY += Utils.ComputeScrollAmount(y, maxY, buttonSizeY, App.AverageTickRate * ScrollSpeedFactor, false);

            ClampScroll();
        }

        private void UpdateSlider(int x, int y, bool final)
        {
            if (!final)
            {
                UpdateSliderValue(sliderDragButton, x, y, false);
                MarkDirty();
            }
            else
            {
                App.UndoRedoManager.EndTransaction();
            }
        }

        private void UpdateScrollBar(int x, int y)
        {
            scrollY = captureScrollY + ((y - captureMouseY) * virtualSizeY / Height);
            ClampScroll();
            MarkDirty();
        }

        private void UpdateDragSample(int x, int y, bool final)
        {
            if (!ClientRectangle.Contains(x, y))
            {
                if (final)
                {
                    var mappingNote = App.GetDPCMSampleMappingNoteAtPos(PointToScreen(new Point(x, y)));
                    if (App.Project.NoteSupportsDPCM(mappingNote))
                    {
                        App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamplesMapping, TransactionFlags.StopAudio);
                        App.Project.UnmapDPCMSample(mappingNote);
                        App.Project.MapDPCMSample(mappingNote, draggedSample);
                        App.UndoRedoManager.EndTransaction();

                        DPCMSampleMapped?.Invoke(draggedSample, PointToScreen(new Point(x, y)));
                    }
                }
                else if (PlatformUtils.IsDesktop)
                {
                    DPCMSampleDraggedOutside?.Invoke(draggedSample, PointToScreen(new Point(x, y)));
                }
            }
        }

        private void UpdateDragSong(int x, int y, bool final)
        {
            if (final)
            {
                var buttonIdx = GetButtonAtCoord(x, y - buttonSizeY / 2, out _);

                if (buttonIdx >= 0)
                {
                    var button = buttons[buttonIdx];

                    if (button.type == ButtonType.Song ||
                        button.type == ButtonType.SongHeader)
                    {
                        var songBefore = buttons[buttonIdx].song;
                        if (songBefore != draggedSong)
                        {
                            App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);
                            App.Project.MoveSong(draggedSong, songBefore);
                            App.UndoRedoManager.EndTransaction();

                            RefreshButtons();
                        }
                    }
                }
            }
            else
            {
                ScrollIfNearEdge(x, y);
                MarkDirty();
            }
        }

        private void UpdateDragInstrument(int x, int y, bool final)
        {
            if (final)
            {
                if (ClientRectangle.Contains(x, y))
                {
                    var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType);

                    var instrumentSrc = draggedInstrument;
                    var instrumentDst = buttonIdx >= 0 && buttons[buttonIdx].type == ButtonType.Instrument ? buttons[buttonIdx].instrument : null;

                    if (instrumentSrc != instrumentDst && instrumentSrc != null && instrumentDst != null)
                    {
                        if (instrumentSrc.Expansion == instrumentDst.Expansion)
                        {
                            if (envelopeDragIdx == -1)
                            {
                                const string label = "Which action do you wish to perform?";

                                var messageDlg = new PropertyDialog("Copy or Replace?", 400, true, true);
                                messageDlg.Properties.AddLabel(null, label, true); // 0
                                messageDlg.Properties.AddRadioButton(PlatformUtils.IsMobile ? label : null, $"Replace all notes of instrument '{instrumentDst.Name}' with '{instrumentSrc.Name}'.", true); // 1
                                messageDlg.Properties.AddRadioButton(PlatformUtils.IsMobile ? label : null, $"Copy all properties and envelopes of instrument '{instrumentSrc.Name}' on to instrument '{instrumentDst.Name}'.", false); // 2
                                messageDlg.Properties.SetPropertyVisible(0, PlatformUtils.IsDesktop);
                                messageDlg.Properties.Build();
                                messageDlg.ShowDialogAsync(null, (r) =>
                                {
                                    if (r == DialogResult.OK)
                                    {
                                        var replace = messageDlg.Properties.GetPropertyValue<bool>(1);
                                        App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);

                                        if (replace)
                                            App.Project.ReplaceInstrument(instrumentDst, instrumentSrc);
                                        else
                                            App.Project.CopyInstrument(instrumentDst, instrumentSrc);

                                        App.UndoRedoManager.EndTransaction();
                                        RefreshButtons();
                                        InstrumentReplaced?.Invoke(instrumentDst);
                                    }
                                });
                            }
                            else
                            {
                                PlatformUtils.MessageBoxAsync($"Are you sure you want to copy the {EnvelopeType.Names[envelopeDragIdx]} envelope of instrument '{instrumentSrc.Name}' to '{instrumentDst.Name}'?", "Copy Envelope", MessageBoxButtons.YesNo, (r) =>
                                {
                                    if (r == DialogResult.Yes)
                                    {
                                        App.UndoRedoManager.BeginTransaction(TransactionScope.Instrument, instrumentDst.Id);
                                        instrumentDst.Envelopes[envelopeDragIdx] = instrumentSrc.Envelopes[envelopeDragIdx].ShallowClone();
                                        instrumentDst.Envelopes[envelopeDragIdx].ClampToValidRange(instrumentDst, envelopeDragIdx);

                                        // HACK : Copy some envelope related stuff. Need to cleanup the envelope code.
                                        switch (envelopeDragIdx)
                                        {
                                            case EnvelopeType.FdsWaveform:
                                                instrumentDst.FdsWavePreset = instrumentSrc.FdsWavePreset;
                                                break;
                                            case EnvelopeType.FdsModulation:
                                                instrumentDst.FdsModPreset = instrumentSrc.FdsModPreset;
                                                break;
                                            case EnvelopeType.N163Waveform:
                                                instrumentDst.N163WavePreset = instrumentSrc.N163WavePreset;
                                                instrumentDst.N163WaveSize = instrumentSrc.N163WaveSize;
                                                break;
                                        }

                                        App.UndoRedoManager.EndTransaction();
                                        if (PlatformUtils.IsDesktop)
                                            App.StartEditInstrument(instrumentDst, envelopeDragIdx);
                                    }
                                });
                            }
                        }
                        else
                        {
                            App.DisplayNotification($"Incompatible audio expansion!"); ;
                        }
                    }
                }
                else if (PlatformUtils.IsDesktop)
                {
                    InstrumentDroppedOutside(draggedInstrument, PointToScreen(new Point(x, y)));
                }
            }
            else
            {
                ScrollIfNearEdge(x, y);
                MarkDirty();
            }
        }

        private void UpdateDragArpeggio(int x, int y, bool final)
        {
            if (final)
            {
                if (ClientRectangle.Contains(x, y))
                {
                    var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType);

                    var arpeggioSrc = draggedArpeggio;
                    var arpeggioDst = buttonIdx >= 0 && buttons[buttonIdx].type == ButtonType.Arpeggio ? buttons[buttonIdx].arpeggio : null;

                    if (arpeggioSrc != arpeggioDst && arpeggioSrc != null && arpeggioDst != null)
                    {
                        if (envelopeDragIdx == -1)
                        {
                            PlatformUtils.MessageBoxAsync($"Are you sure you want to replace all notes using arpeggio '{arpeggioDst.Name}' with '{arpeggioSrc.Name}'?", "Replace arpeggio?", MessageBoxButtons.YesNo, (r) =>
                            {
                                if (r == DialogResult.Yes)
                                {
                                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);
                                    App.Project.ReplaceArpeggio(arpeggioDst, arpeggioSrc);
                                    App.UndoRedoManager.EndTransaction();
                                }
                            });
                        }
                        else
                        {
                            PlatformUtils.MessageBoxAsync($"Are you sure you want to copy the arpeggio values from '{arpeggioSrc.Name}' to '{arpeggioDst.Name}'?", "Copy Arpeggio", MessageBoxButtons.YesNo, (r) =>
                            {
                                if (r == DialogResult.Yes)
                                {
                                    App.UndoRedoManager.BeginTransaction(TransactionScope.Arpeggio, arpeggioDst.Id);
                                    arpeggioDst.Envelope.Length = arpeggioSrc.Envelope.Length;
                                    arpeggioDst.Envelope.Loop = arpeggioSrc.Envelope.Loop;
                                    Array.Copy(arpeggioSrc.Envelope.Values, arpeggioDst.Envelope.Values, arpeggioDst.Envelope.Values.Length);
                                    App.UndoRedoManager.EndTransaction();
                                    if (PlatformUtils.IsDesktop)
                                        App.StartEditArpeggio(arpeggioDst);
                                }
                            });
                        }
                    }
                }
                else if (PlatformUtils.IsDesktop)
                {
                    ArpeggioDroppedOutside?.Invoke(draggedArpeggio, PointToScreen(new Point(x, y)));
                }
            }
            else
            {
                ScrollIfNearEdge(x, y);
                MarkDirty();
            }
        }

        private void UpdateCaptureOperation(int x, int y, bool realTime = false)
        {
            const int CaptureThreshold = PlatformUtils.IsDesktop ? 5 : 50;

            if (captureOperation != CaptureOperation.None && !captureThresholdMet)
            {
                if (Math.Abs(x - captureMouseX) >= CaptureThreshold ||
                    Math.Abs(y - captureMouseY) >= CaptureThreshold)
                {
                    captureThresholdMet = true;
                }
            }

            if (captureOperation != CaptureOperation.None && captureThresholdMet && (captureRealTimeUpdate || !realTime))
            {
                switch (captureOperation)
                {
                    case CaptureOperation.MoveSlider:
                        UpdateSlider(x, y, false);
                        break;
                    case CaptureOperation.ScrollBar:
                        UpdateScrollBar(x, y);
                        break;
                    case CaptureOperation.DragInstrument:
                        UpdateDragInstrument(x, y, false);
                        break;
                    case CaptureOperation.DragArpeggio:
                        UpdateDragArpeggio(x, y, false);
                        break;
                    case CaptureOperation.DragSample:
                        UpdateDragSample(x, y, false);
                        break;
                    case CaptureOperation.DragSong:
                        UpdateDragSong(x, y, false);
                        break;
                    case CaptureOperation.MobilePan:
                        DoScroll(y - mouseLastY);
                        break;
                    default:
                        MarkDirty();
                        break;
                }
            }
        }

        protected void ConditionalShowExpansionIcons(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out _);
            App.SequencerShowExpansionIcons = buttonIdx >= 0 && (buttons[buttonIdx].type == ButtonType.Instrument || buttons[buttonIdx].type == ButtonType.InstrumentHeader);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool middle = e.Button.HasFlag(MouseButtons.Middle) || (e.Button.HasFlag(MouseButtons.Left) && ModifierKeys.HasFlag(Keys.Alt));

            UpdateCursor();
            UpdateCaptureOperation(e.X, e.Y);

            if (middle)
            {
                DoScroll(e.Y - mouseLastY);
            }

            UpdateToolTip(e.X, e.Y);
            ConditionalShowExpansionIcons(e.X, e.Y);

            mouseLastX = e.X;
            mouseLastY = e.Y;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            App.SequencerShowExpansionIcons = false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            EndCaptureOperation(e.X, e.Y);
            UpdateCursor();
            MarkDirty();
        }

        private void StartCaptureOperation(int x, int y, CaptureOperation op, int buttonIdx = -1, int buttonRelX = 0, int buttonRelY = 0)
        {
            Debug.Assert(captureOperation == CaptureOperation.None);
            mouseLastX = x;
            mouseLastY = y;
            captureMouseX = x;
            captureMouseY = y;
            captureButtonIdx = buttonIdx;
            captureButtonRelX = buttonRelX;
            captureButtonRelY = buttonRelY;
            captureScrollY = scrollY;
            Capture = true;
            canFling = false;
            captureOperation = op;
            captureThresholdMet = !captureNeedsThreshold[(int)op];
            captureRealTimeUpdate = captureWantsRealTimeUpdate[(int)op];
        }

        private void EndCaptureOperation(int x, int y)
        {
            if (captureOperation != CaptureOperation.None && captureThresholdMet)
            {
                switch (captureOperation)
                {
                    case CaptureOperation.DragInstrument:
                        UpdateDragInstrument(x, y, true);
                        break;
                    case CaptureOperation.DragArpeggio:
                        UpdateDragArpeggio(x, y, true);
                        break;
                    case CaptureOperation.DragSample:
                        UpdateDragSample(x, y, true);
                        break;
                    case CaptureOperation.MoveSlider:
                        UpdateSlider(x, y, true);
                        break;
                    case CaptureOperation.DragSong:
                        UpdateDragSong(x, y, true);
                        break;
                    case CaptureOperation.MobilePan:
                        canFling = true;
                        break;
                }
            }

            draggedArpeggio = null;
            draggedInstrument = null;
            draggedSample = null;
            draggedSong = null;
            sliderDragButton = null;
            captureOperation = CaptureOperation.None;
            Capture = false;
            MarkDirty();
        }

        private void AbortCaptureOperation()
        {
            if (App.UndoRedoManager.HasTransactionInProgress)
                App.UndoRedoManager.AbortTransaction();

            MarkDirty();

            draggedArpeggio = null;
            draggedInstrument = null;
            draggedSample = null;
            draggedSong = null;
            sliderDragButton = null;
            captureOperation = CaptureOperation.None;
            Capture = false;
            canFling = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            DoScroll(e.Delta > 0 ? buttonSizeY * 3 : -buttonSizeY * 3);
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateRenderCoords();
            ClampScroll();
        }

        bool UpdateSliderValue(Button button, int x, int y, bool mustBeInside)
        {
            var buttonIdx = buttons.IndexOf(button);
            Debug.Assert(buttonIdx >= 0);

            var shift = ModifierKeys.HasFlag(Keys.Shift);
            var buttonTopY = 0;

            foreach (var b in buttons)
            {
                if (b == button)
                    break;

                buttonTopY += b.height;
            }

            var buttonX = x;
            var buttonY = y + scrollY - buttonTopY - topTabSizeY;

            bool insideSlider = (buttonX > (contentSizeX - sliderPosX) &&
                                 buttonX < (contentSizeX - sliderPosX + sliderSizeX) &&
                                 buttonY > (sliderPosY) &&
                                 buttonY < (sliderPosY + sliderSizeY));

            if (mustBeInside && !insideSlider)
                return false;

            var paramVal = button.param.GetValue();

            if (shift)
            {
                var delta = (x - captureMouseX) / 4;
                if (delta != 0)
                {
                    paramVal = Utils.Clamp(paramVal + delta * button.param.SnapValue, button.param.MinValue, button.param.MaxValue);
                    captureMouseX = x;
                }
            }
            else
            {
                paramVal = (int)Math.Round(Utils.Lerp(button.param.MinValue, button.param.MaxValue, Utils.Clamp((buttonX - (contentSizeX - sliderPosX)) / (float)sliderSizeX, 0.0f, 1.0f)));
                captureMouseX = x;
            }

            paramVal = button.param.SnapAndClampValue(paramVal);
            button.param.SetValue(paramVal);

            App.Project.GetPackedSampleData();

            return insideSlider;
        }

        private void ImportSongs()
        {
            Action<string> ImportSongsAction = (filename) =>
            {
                if (filename != null)
                {
                    App.BeginLogTask();

                    Project otherProject = App.OpenProjectFile(filename, false);

                    if (otherProject != null)
                    {
                        var songNames = new List<string>();
                        foreach (var song in otherProject.Songs)
                            songNames.Add(song.Name);

                        var dlg = new PropertyDialog("Import Songs", 300);
                        dlg.Properties.AddLabel(null, "Select songs to import:"); // 0
                        dlg.Properties.AddCheckBoxList(null, songNames.ToArray(), null); // 1
                        dlg.Properties.AddButton(null, "Select All"); // 2
                        dlg.Properties.AddButton(null, "Select None"); // 3
                        dlg.Properties.PropertyClicked += ImportSongs_PropertyClicked;
                        dlg.Properties.Build();

                        dlg.ShowDialogAsync(ParentForm, (r) =>
                        {
                            if (r == DialogResult.OK)
                            {
                                App.UndoRedoManager.BeginTransaction(TransactionScope.Project);

                                var selected = dlg.Properties.GetPropertyValue<bool[]>(1);
                                var songIds = new List<int>();

                                for (int i = 0; i < selected.Length; i++)
                                {
                                    if (selected[i])
                                        songIds.Add(otherProject.Songs[i].Id);
                                }

                                bool success = false;
                                if (songIds.Count > 0)
                                {
                                    otherProject.DeleteAllSongsBut(songIds.ToArray());
                                    success = App.Project.MergeProject(otherProject);
                                }

                                App.UndoRedoManager.AbortOrEndTransaction(success);
                                RefreshButtons();

                                if (!success && PlatformUtils.IsMobile && Log.GetLastMessage(LogSeverity.Error) != null)
                                {
                                    PlatformUtils.DelayedMessageBoxAsync(Log.GetLastMessage(LogSeverity.Error), "Error");
                                }
                            }
                        });
                    }
                    App.EndLogTask();
                }
            };

            if (PlatformUtils.IsMobile)
            {
                MobileProjectDialog dlg = new MobileProjectDialog(App, "Import Songs", false, false);
                dlg.ShowDialogAsync((f) => ImportSongsAction(f));
            }
            else
            {
                var filename = PlatformUtils.ShowOpenFileDialog("Open File", "All Song Files (*.fms;*.txt;*.ftm)|*.fms;*.txt;*.ftm|FamiStudio Files (*.fms)|*.fms|FamiTracker Files (*.ftm)|*.ftm|FamiTracker Text Export (*.txt)|*.txt|FamiStudio Text Export (*.txt)|*.txt", ref Settings.LastInstrumentFolder);
                ImportSongsAction(filename);
            }
        }

        private void ImportSongs_PropertyClicked(PropertyPage props, ClickType click, int propIdx, int rowIdx, int colIdx)
        {
            if (click == ClickType.Button)
            {
                var newValues = new bool[props.GetPropertyValue<bool[]>(1).Length];

                if (propIdx == 2)
                {
                    for (int i = 0; i < newValues.Length; i++)
                        newValues[i] = true;
                }

                props.UpdateCheckBoxList(1, newValues);
            }
        }

        private void ImportInstruments()
        {
            Action<string> ImportInstrumentsAction = (filename) =>
            {
                if (filename != null)
                {
                    App.BeginLogTask();
                    if (filename.ToLower().EndsWith("fti"))
                    {
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                        var success = new FamitrackerInstrumentFile().CreateFromFile(App.Project, filename) != null;
                        App.UndoRedoManager.AbortOrEndTransaction(success);
                        RefreshButtons();
                    }
                    if (filename.ToLower().EndsWith("bti"))
                    {
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                        var success = new BambootrackerInstrumentFile().CreateFromFile(App.Project, filename) != null;
                        App.UndoRedoManager.AbortOrEndTransaction(success);
                        RefreshButtons();
                    }
                    else
                    {
                        Project instrumentProject = App.OpenProjectFile(filename, false);

                        if (instrumentProject != null)
                        {
                            var hasDpcmInstrument = false;

                            var instruments = new List<Instrument>();
                            var instrumentNames = new List<string>();

                            if (instrumentProject.HasAnyMappedSamples)
                            {
                                hasDpcmInstrument = true;
                                instruments.Add(null);
                                instrumentNames.Add(Project.DPCMInstrumentName);
                            }

                            foreach (var instrument in instrumentProject.Instruments)
                            {
                                instruments.Add(instrument);
                                instrumentNames.Add(instrument.NameWithExpansion);
                            }

                            var dlg = new PropertyDialog("Import Instruments", 300);
                            dlg.Properties.AddLabel(null, "Select instruments to import:"); // 0
                            dlg.Properties.AddCheckBoxList(null, instrumentNames.ToArray(), null); // 1
                            dlg.Properties.AddButton(null, "Select All"); // 2
                            dlg.Properties.AddButton(null, "Select None"); // 3
                            dlg.Properties.Build();
                            dlg.Properties.PropertyClicked += ImportInstrument_PropertyClicked;

                            dlg.ShowDialogAsync(ParentForm, (r) =>
                            {
                                if (r == DialogResult.OK)
                                {
                                    var selected = dlg.Properties.GetPropertyValue<bool[]>(1);
                                    var instrumentsIdsToMerge = new List<int>();

                                    for (int i = hasDpcmInstrument ? 1 : 0; i < selected.Length; i++)
                                    {
                                        if (selected[i])
                                            instrumentsIdsToMerge.Add(instruments[i].Id);
                                    }

                                    // Wipe everything but the instruments we want.
                                    if (!hasDpcmInstrument || selected[0] == false)
                                        instrumentProject.DeleteAllSamples();
                                    instrumentProject.DeleteAllSongs();
                                    instrumentProject.DeleteAllArpeggios();
                                    instrumentProject.DeleteAllInstrumentBut(instrumentsIdsToMerge.ToArray());
                                    instrumentProject.DeleteUnmappedSamples();

                                    App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                                    var success = App.Project.MergeProject(instrumentProject);
                                    App.UndoRedoManager.AbortOrEndTransaction(success);
                                    RefreshButtons();
                                }
                            });
                        }
                    }
                    App.EndLogTask();
                }
            };

            if (PlatformUtils.IsMobile)
            {
                MobileProjectDialog dlg = new MobileProjectDialog(App, "Import Instruments", false, false);
                dlg.ShowDialogAsync((f) => ImportInstrumentsAction(f));
            }
            else
            {
                var filename = PlatformUtils.ShowOpenFileDialog("Open File", "All Instrument Files (*.fti;*.fms;*.txt;*.ftm;*.bti)|*.fti;*.fms;*.txt;*.ftm;*.bti|FamiTracker Instrument File (*.fti)|*.fti|BambooTracker Instrument File (*.bti)|*.bti|FamiStudio Files (*.fms)|*.fms|FamiTracker Files (*.ftm)|*.ftm|FamiTracker Text Export (*.txt)|*.txt|FamiStudio Text Export (*.txt)|*.txt", ref Settings.LastInstrumentFolder);
                ImportInstrumentsAction(filename);
            }
        }

        private void ImportInstrument_PropertyClicked(PropertyPage props, ClickType click, int propIdx, int rowIdx, int colIdx)
        {
            if (click == ClickType.Button)
            {
                var newValues = new bool[props.GetPropertyValue<bool[]>(1).Length];

                if (propIdx == 2)
                {
                    for (int i = 0; i < newValues.Length; i++)
                        newValues[i] = true;
                }

                props.UpdateCheckBoxList(1, newValues);
            }
        }

        private void LoadDPCMSample()
        {
            Action<string[]> LoadDPCMSampleAction = (filenames) =>
            {
                if (filenames != null && filenames.Length > 0)
                {
                    var numFamiStudioFiles = 0;
                    var numSamplesFiles = 0;
                    foreach (var fn in filenames)
                    {
                        var ext = Path.GetExtension(fn).ToLower();

                        if (ext == ".fms" && PlatformUtils.IsDesktop)
                            numFamiStudioFiles++;
                        else if (ext == ".dmc" || ext == ".wav")
                            numSamplesFiles++;
                    }

                    if (numFamiStudioFiles > 1 || (numFamiStudioFiles == 1 && numSamplesFiles != 0))
                    {
                        PlatformUtils.MessageBoxAsync("You can only select one FamiStudio project to import samples from.", "Error", MessageBoxButtons.OK);
                        return;
                    }
                    else if (numFamiStudioFiles == 1)
                    {
                        Project samplesProject = App.OpenProjectFile(filenames[0], false);

                        if (samplesProject != null)
                        {
                            if (samplesProject.Samples.Count == 0)
                            {
                                PlatformUtils.MessageBox("The selected project does not contain any samples.", "Error", MessageBoxButtons.OK);
                                return;
                            }

                            var samplesNames = new List<string>();

                            foreach (var sample in samplesProject.Samples)
                                samplesNames.Add(sample.Name);

                            var dlg = new PropertyDialog("Import DPCM Samples", 300);
                            dlg.Properties.AddLabel(null, "Select samples to import:"); // 0
                            dlg.Properties.AddCheckBoxList("Import DPCM Samples", samplesNames.ToArray(), null); // 1
                            dlg.Properties.AddButton(null, "Select All"); // 2
                            dlg.Properties.AddButton(null, "Select None"); // 3
                            dlg.Properties.Build();
                            dlg.Properties.PropertyClicked += ImportInstrument_PropertyClicked;

                            dlg.ShowDialogAsync(ParentForm, (r) =>
                            {
                                if (r == DialogResult.OK)
                                {
                                    App.BeginLogTask();
                                    {
                                        var selected = dlg.Properties.GetPropertyValue<bool[]>(1);
                                        var sampleIdsToMerge = new List<int>();

                                        for (int i = 0; i < selected.Length; i++)
                                        {
                                            if (selected[i])
                                                sampleIdsToMerge.Add(samplesProject.Samples[i].Id);
                                        }

                                        // Wipe everything but the instruments we want.
                                        samplesProject.DeleteAllSongs();
                                        samplesProject.DeleteAllArpeggios();
                                        samplesProject.DeleteAllSamplesBut(sampleIdsToMerge.ToArray());
                                        samplesProject.DeleteAllInstruments();
                                        samplesProject.DeleteAllMappings();

                                        App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamples);
                                        bool success = App.Project.MergeProject(samplesProject);
                                        App.UndoRedoManager.AbortOrEndTransaction(success);
                                    }
                                    App.EndLogTask();
                                    RefreshButtons();
                                }
                            });
                        }
                    }
                    else if (numSamplesFiles > 0)
                    {
                        App.BeginLogTask();
                        {
                            App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamples);

                            foreach (var filename in filenames)
                            {
                                var sampleName = Path.GetFileNameWithoutExtension(filename);
                                if (sampleName.Length > 16)
                                    sampleName = sampleName.Substring(0, 16);
                                sampleName = App.Project.GenerateUniqueDPCMSampleName(sampleName);

                                if (Path.GetExtension(filename).ToLower() == ".wav")
                                {
                                    var wavData = WaveFile.Load(filename, out var sampleRate);
                                    if (wavData != null)
                                    {
                                        var maximumSamples = sampleRate * 2;
                                        if (wavData.Length > maximumSamples)
                                        {
                                            Array.Resize(ref wavData, maximumSamples);
                                            Log.LogMessage(LogSeverity.Warning, "The maximum supported length for a WAV file is 2.0 seconds. Truncating.");
                                        }

                                        App.Project.CreateDPCMSampleFromWavData(sampleName, wavData, sampleRate, filename);
                                    }
                                }
                                else if (Path.GetExtension(filename).ToLower() == ".dmc")
                                {
                                    var dmcData = File.ReadAllBytes(filename);
                                    if (dmcData.Length > DPCMSample.MaxSampleSize)
                                    {
                                        Array.Resize(ref dmcData, DPCMSample.MaxSampleSize);
                                        Log.LogMessage(LogSeverity.Warning, $"The maximum supported size for a DMC is {DPCMSample.MaxSampleSize} bytes. Truncating.");
                                    }
                                    App.Project.CreateDPCMSampleFromDmcData(sampleName, dmcData, filename);
                                }
                            }

                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons();
                        }
                        App.EndLogTask();
                    }
                }
            };

            if (PlatformUtils.IsMobile)
            {
                PlatformUtils.StartMobileLoadFileOperationAsync("*/*", (f) => LoadDPCMSampleAction(new[] { f }));
            }
            else
            {
                var filenames = PlatformUtils.ShowOpenFileDialog("Open File", "All Sample Files (*.wav;*.dmc;*.fms)|*.wav;*.dmc;*.fms|Wav Files (*.wav)|*.wav|DPCM Sample Files (*.dmc)|*.dmc|FamiStudio Files (*.fms)|*.fms", ref Settings.LastSampleFolder, true);
                LoadDPCMSampleAction(filenames);
            }
        }

        private void AddSong()
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, TransactionFlags.StopAudio);
            App.SelectedSong = App.Project.CreateSong();
            App.UndoRedoManager.EndTransaction();
            RefreshButtons();
        }

        private void AskDeleteSong(Song song)
        {
            PlatformUtils.MessageBoxAsync($"Are you sure you want to delete '{song.Name}' ?", "Delete song", MessageBoxButtons.YesNo, (r) =>
            {
                if (r == DialogResult.Yes)
                {
                    bool selectNewSong = song == App.SelectedSong;
                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, TransactionFlags.StopAudio);
                    App.Project.DeleteSong(song);
                    if (selectNewSong)
                        App.SelectedSong = App.Project.Songs[0];
                    App.UndoRedoManager.EndTransaction();
                    RefreshButtons();
                }
            });
        }

        private void AddInstrument(int expansionType)
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);
            App.SelectedInstrument = App.Project.CreateInstrument(expansionType);
            App.UndoRedoManager.EndTransaction();
            RefreshButtons();
        }

        private void AskAddInstrument(int x, int y)
        {
            var instrumentType = ExpansionType.None;

            if (App.Project.NeedsExpansionInstruments)
            {
                var activeExpansions = App.Project.GetActiveExpansions();
                var expNames = new List<string>();

                var dlg = new PropertyDialog("Add Instrument", PointToScreen(new Point(x, y)), 260, true);
                dlg.Properties.AddLabel(null, "Select audio expansion:"); // 0

                expNames.Add(ExpansionType.Names[ExpansionType.None]);
                dlg.Properties.AddRadioButton(PlatformUtils.IsMobile ? "Select audio expansion" : null, expNames[0], true);

                for (int i = 1; i < activeExpansions.Length; i++)
                {
                    if (ExpansionType.NeedsExpansionInstrument(activeExpansions[i]))
                    {
                        var expName = ExpansionType.Names[activeExpansions[i]];
                        dlg.Properties.AddRadioButton(null, expName, false);
                        expNames.Add(expName);
                    }
                }

                dlg.Properties.SetPropertyVisible(0, PlatformUtils.IsDesktop);
                dlg.Properties.Build();

                dlg.ShowDialogAsync(ParentForm, (r) =>
                {
                    if (r == DialogResult.OK)
                    {
                        for (int i = 0; i < expNames.Count; i++)
                        {
                            if (dlg.Properties.GetPropertyValue<bool>(i + 1))
                            {
                                instrumentType = ExpansionType.GetValueForName(expNames[i]);
                                break;
                            }
                        }

                        AddInstrument(instrumentType);
                    }
                });
            }
            else
            {
                AddInstrument(instrumentType);
            }
        }

        private void ToggleExpandInstrument(Instrument inst)
        {
            expandedInstrument = expandedInstrument == inst ? null : inst;
            selectedInstrumentTab = null;
            expandedSample = null;
            RefreshButtons(false);
        }

        private void AskDeleteInstrument(Instrument inst)
        {
            PlatformUtils.MessageBoxAsync($"Are you sure you want to delete '{inst.Name}' ? All notes using this instrument will be deleted.", "Delete instrument", MessageBoxButtons.YesNo, (r) =>
            {
                if (r == DialogResult.Yes)
                {
                    bool selectNewInstrument = inst == App.SelectedInstrument;
                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, TransactionFlags.StopAudio);
                    App.Project.DeleteInstrument(inst);
                    if (selectNewInstrument)
                        App.SelectedInstrument = App.Project.Instruments.Count > 0 ? App.Project.Instruments[0] : null;
                    InstrumentDeleted?.Invoke(inst);
                    App.UndoRedoManager.EndTransaction();
                    RefreshButtons();
                }
            });
        }

        private void ClearInstrumentEnvelope(Instrument inst, int envelopeType)
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.Instrument, inst.Id);
            inst.Envelopes[envelopeType].ClearToDefault(envelopeType);
            App.UndoRedoManager.EndTransaction();
            MarkDirty();
        }

        private void AddArpeggio()
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);
            App.SelectedArpeggio = App.Project.CreateArpeggio();
            App.UndoRedoManager.EndTransaction();
            RefreshButtons();
        }

        private void AskDeleteArpeggio(Arpeggio arpeggio)
        {
            PlatformUtils.MessageBoxAsync($"Are you sure you want to delete '{arpeggio.Name}' ? All notes using this arpeggio will be no longer be arpeggiated.", "Delete arpeggio", MessageBoxButtons.YesNo, (r) =>
            {
                if (r == DialogResult.Yes)
                {
                    bool selectNewArpeggio = arpeggio == App.SelectedArpeggio;
                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, TransactionFlags.StopAudio);
                    App.Project.DeleteArpeggio(arpeggio);
                    if (selectNewArpeggio)
                        App.SelectedArpeggio = App.Project.Arpeggios.Count > 0 ? App.Project.Arpeggios[0] : null;
                    ArpeggioDeleted?.Invoke(arpeggio);
                    App.UndoRedoManager.EndTransaction();
                    RefreshButtons();
                }
            });
        }

        private void ReloadDPCMSampleSourceData(DPCMSample sample)
        {
            if (!string.IsNullOrEmpty(sample.SourceFilename))
            {
                if (File.Exists(sample.SourceFilename))
                {
                    if (sample.SourceDataIsWav)
                    {
                        var wavData = WaveFile.Load(sample.SourceFilename, out var sampleRate);
                        if (wavData != null)
                        {
                            var maximumSamples = sampleRate * 2;
                            if (wavData.Length > maximumSamples)
                                Array.Resize(ref wavData, maximumSamples);

                            App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamples);
                            sample.SetWavSourceData(wavData, sampleRate, sample.SourceFilename, false);
                            sample.Process();
                            App.UndoRedoManager.EndTransaction();
                        }
                    }
                    else
                    {
                        var dmcData = File.ReadAllBytes(sample.SourceFilename);
                        if (dmcData.Length > DPCMSample.MaxSampleSize)
                            Array.Resize(ref dmcData, DPCMSample.MaxSampleSize);

                        App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamples);
                        sample.SetDmcSourceData(dmcData, sample.SourceFilename, false);
                        sample.Process();
                        App.UndoRedoManager.EndTransaction();
                    }

                    DPCMSampleReloaded?.Invoke(sample);
                }
                else
                {
                    App.DisplayNotification($"Cannot find source file '{sample.SourceFilename}'!"); ;
                }
            }
        }

        private void ExportDPCMSampleProcessedData(DPCMSample sample)
        {
            var filename = PlatformUtils.ShowSaveFileDialog("Save File", "DPCM Samples (*.dmc)|*.dmc", ref Settings.LastSampleFolder);
            if (filename != null)
                File.WriteAllBytes(filename, sample.ProcessedData);
        }

        private void ExportDPCMSampleSourceData(DPCMSample sample)
        {
            if (sample.SourceDataIsWav)
            {
                var filename = PlatformUtils.ShowSaveFileDialog("Save File", "Wav file (*.wav)|*.wav", ref Settings.LastSampleFolder);
                if (filename != null)
                    WaveFile.Save(sample.SourceWavData.Samples, filename, sample.SourceWavData.SampleRate, 1);
            }
            else
            {
                var filename = PlatformUtils.ShowSaveFileDialog("Save File", "DPCM Samples (*.dmc)|*.dmc", ref Settings.LastSampleFolder);
                if (filename != null)
                    File.WriteAllBytes(filename, sample.SourceDmcData.Data);
            }
        }

        private void ToggleExpandDPCMSample(DPCMSample sample)
        {
            expandedSample = expandedSample == sample ? null : sample;
            expandedInstrument = null;
            selectedInstrumentTab = null;
            RefreshButtons();
        }

        private void AskDeleteDPCMSample(DPCMSample sample)
        {
            PlatformUtils.MessageBoxAsync($"Are you sure you want to delete DPCM Sample '{sample.Name}' ? It will be removed from the DPCM Instrument and every note using it will be silent.", "Delete DPCM Sample", MessageBoxButtons.YesNo, (r) =>
            {
                if (r == DialogResult.Yes)
                {
                    App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSamples, TransactionFlags.StopAudio);
                    App.Project.DeleteSample(sample);
                    DPCMSampleDeleted?.Invoke(sample);
                    App.UndoRedoManager.EndTransaction();
                    RefreshButtons();
                }
            });
        }

        private bool HandleMouseDownPan(MouseEventArgs e)
        {
            bool middle = e.Button.HasFlag(MouseButtons.Middle) || (e.Button.HasFlag(MouseButtons.Left) && ModifierKeys.HasFlag(Keys.Alt));

            if (middle)
            {
                mouseLastY = e.Y;
                return true;
            }

            return false;
        }

        private bool HandleMouseDownScrollbar(MouseEventArgs e)
        {
            if (e.Button.HasFlag(MouseButtons.Left) && needsScrollBar && e.X > contentSizeX && GetScrollBarParams(out var scrollBarPosY, out var scrollBarSizeY))
            {
                if (e.Y < scrollBarPosY)
                {
                    scrollY -= Height;
                    ClampScroll();
                    MarkDirty();
                }
                else if (e.Y > (scrollBarPosY + scrollBarSizeY))
                {
                    scrollY += Height;
                    ClampScroll();
                    MarkDirty();
                }
                else
                {
                    StartCaptureOperation(e.X, e.Y, CaptureOperation.ScrollBar);
                }

                return true;
            }

            return false;
        }

        private bool HandleMouseDownSongProjectSettings(MouseEventArgs e, SubButtonType subButtonType)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Properties)
                    EditProjectProperties(new Point(e.X, e.Y));
            }

            return true;
        }

        private bool HandleMouseDownSongHeaderButton(MouseEventArgs e, SubButtonType subButtonType)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Add)
                    AddSong();
                else if (subButtonType == SubButtonType.Load)
                    ImportSongs();
            }

            return true;
        }

        private bool HandleMouseDownSongButton(MouseEventArgs e, Button button, int buttonIdx, SubButtonType subButtonType)
        {
            var left  = e.Button.HasFlag(MouseButtons.Left);
            var right = e.Button.HasFlag(MouseButtons.Right);

            if (left && subButtonType == SubButtonType.Properties)
            {
                EditSongProperties(new Point(e.X, e.Y), button.song);
            }
            else if (left && subButtonType == SubButtonType.Max)
            {
                App.SelectedSong = button.song;
                StartCaptureOperation(e.X, e.Y, CaptureOperation.DragSong, buttonIdx);
                draggedSong = button.song;
            }
            else if (right && App.Project.Songs.Count > 1)
            {
                AskDeleteSong(button.song);
            }

            return true;
        }

        private bool HandleMouseDownInstrumentHeaderButton(MouseEventArgs e, SubButtonType subButtonType)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Add)
                    AskAddInstrument(e.X, e.Y);
                if (subButtonType == SubButtonType.Load)
                    ImportInstruments();
            }

            return true;
        }

        private bool HandleMouseDownInstrumentButton(MouseEventArgs e, Button button, SubButtonType subButtonType, int buttonIdx, int buttonRelX, int buttonRelY)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Expand)
                {
                    ToggleExpandInstrument(button.instrument);
                    return true;
                }
                else if (subButtonType == SubButtonType.DPCM)
                {
                    App.StartEditInstrument(button.instrument, EnvelopeType.Count);
                    return true;
                }
                else if (subButtonType == SubButtonType.Properties)
                {
                    EditInstrumentProperties(new Point(e.X, e.Y), button.instrument);
                    return true;
                }

                App.SelectedInstrument = button.instrument;

                if (button.instrument != null)
                {
                    envelopeDragIdx = -1;
                    draggedInstrument = button.instrument;
                    StartCaptureOperation(e.X, e.Y, CaptureOperation.DragInstrument, buttonIdx, buttonRelX, buttonRelY);
                }

                if (subButtonType < SubButtonType.EnvelopeMax)
                {
                    App.StartEditInstrument(button.instrument, (int)subButtonType);
                    envelopeDragIdx = (int)subButtonType;
                }
            }
            else if (e.Button.HasFlag(MouseButtons.Right) && button.instrument != null)
            {
                if (subButtonType < SubButtonType.EnvelopeMax)
                    ClearInstrumentEnvelope(button.instrument, (int)subButtonType);
                else if (subButtonType == SubButtonType.Max)
                    AskDeleteInstrument(button.instrument);
            }

            return true;
        }

        private bool StartMoveSlider(int x, int y, Button button, int buttonIdx)
        {
            App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);
            captureMouseX = x; // Hack, UpdateSliderValue relies on this.

            if (UpdateSliderValue(button, x, y, true))
            {
                sliderDragButton = button;
                StartCaptureOperation(x, y, CaptureOperation.MoveSlider, buttonIdx);
                MarkDirty();
                return true;
            }
            else
            {
                App.UndoRedoManager.AbortTransaction();
                return false;
            }
        }

        private bool HandleMouseDownParamSliderButton(MouseEventArgs e, Button button, int buttonIdx)
        {
            bool left  = e.Button.HasFlag(MouseButtons.Left);
            bool right = e.Button.HasFlag(MouseButtons.Right);

            if (left || right)
            {
                if (left)
                {
                    StartMoveSlider(e.X, e.Y, button, buttonIdx);
                }
                else
                {
                    App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);
                    button.param.SetValue(button.param.DefaultValue);
                    App.UndoRedoManager.EndTransaction();
                    MarkDirty();
                }
            }

            return true;
        }

        private void ClickParamCheckbox(int x, int y, Button button, bool reset)
        {
            if (x >= contentSizeX - checkBoxPosX)
            {
                App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);

                if (!reset)
                {
                    var val = button.param.GetValue();
                    button.param.SetValue(val == 0 ? 1 : 0);
                }
                else
                {
                    button.param.SetValue(button.param.DefaultValue);
                }

                App.UndoRedoManager.EndTransaction();
                MarkDirty();
            }
        }

        private void ClickParamListButton(int x, int y, Button button, bool reset)
        {
            var buttonWidth = ScaleCustom(bmpMiscAtlas.GetElementSize((int)MiscImageIndices.ButtonLeft).Width, bitmapScale);
            var buttonX = x;
            var leftButton  = buttonX > (contentSizeX - sliderPosX) && buttonX < (contentSizeX - sliderPosX + buttonWidth);
            var rightButton = buttonX > (contentSizeX - sliderPosX + sliderSizeX - buttonWidth) && buttonX < (contentSizeX - sliderPosX + sliderSizeX);
            var delta = leftButton ? -1 : (rightButton ? 1 : 0);

            if (!reset && (leftButton || rightButton))
            {
                App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);

                var val = button.param.GetValue();

                if (rightButton)
                    val = button.param.SnapAndClampValue(button.param.GetValue() + 1);
                else
                    val = button.param.SnapAndClampValue(button.param.GetValue() - 1);

                button.param.SetValue(val);

                App.UndoRedoManager.EndTransaction();
                MarkDirty();
            }
            else if (reset && buttonX > (contentSizeX - sliderPosX))
            {
                App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);
                button.param.SetValue(button.param.DefaultValue);
                App.UndoRedoManager.EndTransaction();
                MarkDirty();
            }
        }

        private void ClickParamTabsButton(int x, int y, Button button)
        {
            var tabWidth = Utils.DivideAndRoundUp(contentSizeX - expandButtonSizeX - paramRightPadX, button.tabNames.Length);
            var tabIndex = Utils.Clamp((x - expandButtonSizeX) / tabWidth, 0, button.tabNames.Length - 1);

            selectedInstrumentTab = button.tabNames[tabIndex];

            RefreshButtons();
        }

        private bool HandleMouseDownParamCheckboxButton(MouseEventArgs e, Button button)
        {
            bool left  = e.Button.HasFlag(MouseButtons.Left);
            bool right = e.Button.HasFlag(MouseButtons.Right);

            if (left || right)
                ClickParamCheckbox(e.X, e.Y, button, right);

            return true;
        }

        private bool HandleMouseDownParamListButton(MouseEventArgs e, Button button)
        {
            bool left  = e.Button.HasFlag(MouseButtons.Left);
            bool right = e.Button.HasFlag(MouseButtons.Right);

            if (left || right)
                ClickParamListButton(e.X, e.Y, button, right);

            return true;
        }

        private bool HandleMouseDownParamTabs(MouseEventArgs e, Button button)
        {
            bool left = e.Button.HasFlag(MouseButtons.Left);
            bool right = e.Button.HasFlag(MouseButtons.Right);

            if (left)
                ClickParamTabsButton(e.X, e.Y, button);

            return true;
        }

        private bool HandleMouseDownArpeggioHeaderButton(MouseEventArgs e, SubButtonType subButtonType)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Add)
                    AddArpeggio();
            }

            return true;
        }

        private bool HandleMouseDownArpeggioButton(MouseEventArgs e, Button button, SubButtonType subButtonType, int buttonIdx, int buttonRelX, int buttonRelY)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.Properties)
                {
                    EditArpeggioProperties(new Point(e.X, e.Y), button.arpeggio);
                    return true;
                }

                App.SelectedArpeggio = button.arpeggio;

                envelopeDragIdx = -1;
                draggedArpeggio = button.arpeggio;
                StartCaptureOperation(e.X, e.Y, CaptureOperation.DragArpeggio, buttonIdx, buttonRelX, buttonRelY);

                if (subButtonType < SubButtonType.EnvelopeMax)
                {
                    envelopeDragIdx = (int)subButtonType;
                    App.StartEditArpeggio(button.arpeggio);
                }
            }
            else if (e.Button.HasFlag(MouseButtons.Right) && button.arpeggio != null) 
            {
                AskDeleteArpeggio(button.arpeggio);
            }

            return true;
        }

        private bool HandleMouseDownDpcmHeaderButton(MouseEventArgs e, SubButtonType subButtonType)
        {
            if (e.Button.HasFlag(MouseButtons.Left) && subButtonType == SubButtonType.Load)
            {
                LoadDPCMSample();
            }

            return true;
        }

        private bool HandleMouseDownDpcmButton(MouseEventArgs e, Button button, SubButtonType subButtonType, int buttonIdx)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                if (subButtonType == SubButtonType.EditWave)
                {
                    App.StartEditDPCMSample(button.sample);
                }
                else if (subButtonType == SubButtonType.Reload)
                {
                    ReloadDPCMSampleSourceData(button.sample);
                }
                else if (subButtonType == SubButtonType.Save)
                {
                    ExportDPCMSampleProcessedData(button.sample);
                }
                else if (subButtonType == SubButtonType.Play)
                {
                    App.PreviewDPCMSample(button.sample, false);
                }
                else if (subButtonType == SubButtonType.Properties)
                {
                    EditDPCMSampleProperties(new Point(e.X, e.Y), button.sample);
                }
                else if (subButtonType == SubButtonType.Expand)
                {
                    ToggleExpandDPCMSample(button.sample);
                }
                else if (subButtonType == SubButtonType.Max)
                {
                    draggedSample = button.sample;
                    StartCaptureOperation(e.X, e.Y, CaptureOperation.DragSample, buttonIdx);
                    MarkDirty();
                }
            }
            else if (e.Button.HasFlag(MouseButtons.Right))
            {
                if (subButtonType == SubButtonType.Play)
                {
                    App.PreviewDPCMSample(button.sample, true);
                }
                else if (subButtonType == SubButtonType.Save)
                {
                    ExportDPCMSampleSourceData(button.sample);
                }
                else if (subButtonType == SubButtonType.Max)
                {
                    AskDeleteDPCMSample(button.sample);
                }
            }

            return true;
        }

        private bool HandleMouseDownTopTabs(MouseEventArgs e)
        {
            if (topTabSizeY > 0 && e.Y < topTabSizeY)
            {
                selectedTab = e.X < Width / 2 ? TabType.Project : TabType.Registers;
                RefreshButtons();
                return true;
            }

            return false;
        }

        private bool HandleMouseDownButtons(MouseEventArgs e)
        {
            var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];

                switch (button.type)
                {
                    case ButtonType.ProjectSettings:
                        return HandleMouseDownSongProjectSettings(e, subButtonType);
                    case ButtonType.SongHeader:
                        return HandleMouseDownSongHeaderButton(e, subButtonType);
                    case ButtonType.Song:
                        return HandleMouseDownSongButton(e, button, buttonIdx, subButtonType);
                    case ButtonType.InstrumentHeader:
                        return HandleMouseDownInstrumentHeaderButton(e, subButtonType);
                    case ButtonType.Instrument:
                        return HandleMouseDownInstrumentButton(e, button, subButtonType, buttonIdx, buttonRelX, buttonRelY);
                    case ButtonType.ParamSlider:
                        return HandleMouseDownParamSliderButton(e, button, buttonIdx);
                    case ButtonType.ParamCheckbox:
                        return HandleMouseDownParamCheckboxButton(e, button);
                    case ButtonType.ParamList:
                        return HandleMouseDownParamListButton(e, button);
                    case ButtonType.ParamTabs:
                        return HandleMouseDownParamTabs(e, button);
                    case ButtonType.ArpeggioHeader:
                        return HandleMouseDownArpeggioHeaderButton(e, subButtonType);
                    case ButtonType.Arpeggio:
                        return HandleMouseDownArpeggioButton(e, button, subButtonType, buttonIdx, buttonRelX, buttonRelY);
                    case ButtonType.DpcmHeader:
                        return HandleMouseDownDpcmHeaderButton(e, subButtonType);
                    case ButtonType.Dpcm:
                        return HandleMouseDownDpcmButton(e, button, subButtonType, buttonIdx);
                }

                return true;
            }

            return false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (captureOperation != CaptureOperation.None)
                return;

            if (HandleMouseDownPan(e)) goto Handled;
            if (HandleMouseDownScrollbar(e)) goto Handled;
            if (HandleMouseDownTopTabs(e)) goto Handled;
            if (HandleMouseDownButtons(e)) goto Handled;
            return;

        Handled:
            MarkDirty();
        }

        private bool HandleTouchClickProjectSettingsButton(int x, int y, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Properties)
                EditProjectProperties(Point.Empty);

            return true;
        }

        private bool HandleTouchClickSongHeaderButton(int x, int y, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Add)
                AddSong();
            else if (subButtonType == SubButtonType.Load)
                ImportSongs();

            return true;
        }

        private bool HandleTouchClickInstrumentHeaderButton(int x, int y, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Add)
                AskAddInstrument(x, y);
            if (subButtonType == SubButtonType.Load)
                ImportInstruments();

            return true;
        }

        private bool HandleTouchClickSongButton(int x, int y, Button button, int buttonIdx, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Properties)
            {
                EditSongProperties(Point.Empty, button.song);
            }
            else
            {
                App.SelectedSong = button.song;
                if (App.Project.Songs.Count > 1)
                    highlightedButtonIdx = highlightedButtonIdx == buttonIdx ? -1 : buttonIdx;
            }

            return true;
        }

        private bool HandleTouchClickInstrumentButton(int x, int y, Button button, SubButtonType subButtonType, int buttonIdx, int buttonRelX, int buttonRelY)
        {
            if (subButtonType == SubButtonType.Properties)
            {
                if (button.instrument != null)
                    EditInstrumentProperties(Point.Empty, button.instrument);
            }
            else
            {
                App.SelectedInstrument = button.instrument;
                highlightedButtonIdx = highlightedButtonIdx == buttonIdx && subButtonType == SubButtonType.Max || button.instrument == null || subButtonType != SubButtonType.Max ? -1 : buttonIdx;

                if (subButtonType == SubButtonType.Expand)
                {
                    ToggleExpandInstrument(button.instrument);
                }
                else if (subButtonType == SubButtonType.DPCM)
                {
                    App.StartEditInstrument(button.instrument, EnvelopeType.Count);
                }
                else if (subButtonType < SubButtonType.EnvelopeMax)
                {
                    App.StartEditInstrument(button.instrument, (int)subButtonType);
                }
            }

            return true;
        }

        private bool HandleTouchClickArpeggioHeaderButton(int x, int y, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Add)
                AddArpeggio();
            return true;
        }

        private bool HandleTouchClickArpeggioButton(int x, int y, Button button, SubButtonType subButtonType, int buttonIdx, int buttonRelX, int buttonRelY)
        {
            if (subButtonType == SubButtonType.Properties)
            {
                EditArpeggioProperties(Point.Empty, button.arpeggio);
            }
            else
            {
                App.SelectedArpeggio = button.arpeggio;
                if (subButtonType < SubButtonType.EnvelopeMax)
                    App.StartEditArpeggio(button.arpeggio);
            }

            return true;
        }

        private bool HandleTouchClickDpcmHeaderButton(int x, int y, SubButtonType subButtonType)
        {
            if (subButtonType == SubButtonType.Load)
                LoadDPCMSample();
            return true;
        }

        private bool HandleTouchClickDpcmButton(int x, int y, Button button, SubButtonType subButtonType, int buttonIdx)
        {
            if (subButtonType == SubButtonType.EditWave)
            {
                App.StartEditDPCMSample(button.sample);
            }
            else if (subButtonType == SubButtonType.Play)
            {
                App.PreviewDPCMSample(button.sample, false);
            }
            else if (subButtonType == SubButtonType.Properties)
            {
                EditDPCMSampleProperties(Point.Empty, button.sample);
            }
            else if (subButtonType == SubButtonType.Expand)
            {
                ToggleExpandDPCMSample(button.sample);
            }

            return true;
        }

        private bool HandleTouchClickParamCheckboxButton(int x, int y, Button button)
        {
            ClickParamCheckbox(x, y, button, false);
            return true;
        }

        private bool HandleTouchClickParamListButton(int x, int y, Button button)
        {
            ClickParamListButton(x, y, button, false);
            return true;
        }

        private bool HandleTouchClickParamTabsButton(int x, int y, Button button)
        {
            ClickParamTabsButton(x, y, button);
            return true;
        }

        private bool HandleTouchClickButtons(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];

                switch (button.type)
                {
                    case ButtonType.ProjectSettings:
                        return HandleTouchClickProjectSettingsButton(x, y, subButtonType);
                    case ButtonType.SongHeader:
                        return HandleTouchClickSongHeaderButton(x, y, subButtonType);
                    case ButtonType.Song:
                        return HandleTouchClickSongButton(x, y, button, buttonIdx, subButtonType);
                    case ButtonType.InstrumentHeader:
                        return HandleTouchClickInstrumentHeaderButton(x, y, subButtonType);
                    case ButtonType.Instrument:
                        return HandleTouchClickInstrumentButton(x, y, button, subButtonType, buttonIdx, buttonRelX, buttonRelY);
                    case ButtonType.ParamCheckbox:
                        return HandleTouchClickParamCheckboxButton(x, y, button);
                    case ButtonType.ParamList:
                        return HandleTouchClickParamListButton(x, y, button);
                    case ButtonType.ParamTabs:
                        return HandleTouchClickParamTabsButton(x, y, button);
                    case ButtonType.ArpeggioHeader:
                        return HandleTouchClickArpeggioHeaderButton(x, y, subButtonType);
                    case ButtonType.Arpeggio:
                        return HandleTouchClickArpeggioButton(x, y, button, subButtonType, buttonIdx, buttonRelX, buttonRelY);
                    case ButtonType.DpcmHeader:
                        return HandleTouchClickDpcmHeaderButton(x, y, subButtonType);
                    case ButtonType.Dpcm:
                        return HandleTouchClickDpcmButton(x, y, button, subButtonType, buttonIdx);
                        
                }

                return true;
            }

            return false;
        }

        private bool HandleTouchLongPressProjectSettings(int x, int y)
        {    
            App.ShowContextMenu(new[]
            {
                new ContextMenuOption("MenuProperties", "Project Properties...", () => { EditProjectProperties(Point.Empty); })
            });

            return true;
        }

        private bool HandleTouchLongPressSongButton(int x, int y, Button button)
        {
            var menu = new List<ContextMenuOption>();
            menu.Add(new ContextMenuOption("MenuProperties", "Song/Tempo Properties...", () => { EditSongProperties(Point.Empty, button.song); }));
            if (App.Project.Songs.Count > 1)
                menu.Add(new ContextMenuOption("MenuDelete", "Delete Song", () => { AskDeleteSong(button.song); }));
            App.ShowContextMenu(menu.ToArray());
            return true;
        }

        private bool HandleTouchLongPressInstrumentButton(int x, int y, Button button, SubButtonType subButtonType, int buttonIdx)
        {
            var menu = new List<ContextMenuOption>();
            if (button.instrument != null)
            {
                menu.Add(new ContextMenuOption("MenuProperties", "Instrument Properties...", () => { EditInstrumentProperties(Point.Empty, button.instrument); })); 
                menu.Add(new ContextMenuOption("MenuDelete", "Delete Instrument", () => { AskDeleteInstrument(button.instrument); }));
            }
            if (subButtonType < SubButtonType.EnvelopeMax)
            {
                menu.Add(new ContextMenuOption("MenuClearEnvelope", "Clear Envelope", () => { ClearInstrumentEnvelope(button.instrument, (int)subButtonType); }));
            }
            if (menu.Count > 0)
                App.ShowContextMenu(menu.ToArray());
            return true;
        }

        private bool HandleTouchLongPressArpeggioButton(int x, int y, Button button)
        {
            var menu = new List<ContextMenuOption>();
            if (button.arpeggio != null)
            {
                menu.Add(new ContextMenuOption("MenuProperties", "Arpeggio Properties...", () => { EditArpeggioProperties(Point.Empty, button.arpeggio); }));
                menu.Add(new ContextMenuOption("MenuDelete", "Delete Arpeggio", () => { AskDeleteArpeggio(button.arpeggio); }));
            }
            if (menu.Count > 0)
                App.ShowContextMenu(menu.ToArray());
            return true;
        }

        private bool HandleTouchLongPressDpcmButton(int x, int y, Button button)
        {
            App.ShowContextMenu(new[]
            {
                new ContextMenuOption("MenuProperties", "DPCM Sample Properties...", () => { EditDPCMSampleProperties(Point.Empty, button.sample); }),
                new ContextMenuOption("MenuDelete", "Delete DPCM Sample", () => { AskDeleteDPCMSample(button.sample); })
            });

            return true;
        }

        private void ResetParamButtonDefaultValue(Button button)
        {
            App.UndoRedoManager.BeginTransaction(button.paramScope, button.paramObjectId);
            button.param.SetValue(button.param.DefaultValue);
            App.UndoRedoManager.EndTransaction();
            MarkDirty();
        }

        private bool HandleTouchLongPressParamButton(int x, int y, Button button)
        {
            App.ShowContextMenu(new[]
            {
                new ContextMenuOption("MenuReset", "Reset Default Value", () => { ResetParamButtonDefaultValue(button); })
            });

            return true;
        }

        private bool HandleTouchLongPressButtons(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];

                switch (button.type)
                {
                    case ButtonType.ProjectSettings:
                        return HandleTouchLongPressProjectSettings(x, y);
                    case ButtonType.Song:
                        return HandleTouchLongPressSongButton(x, y, button);
                    case ButtonType.Instrument:
                        return HandleTouchLongPressInstrumentButton(x, y, button, subButtonType, buttonIdx);
                    case ButtonType.ParamSlider:
                    case ButtonType.ParamCheckbox:
                    case ButtonType.ParamList:
                        return HandleTouchLongPressParamButton(x, y, button);
                    case ButtonType.Arpeggio:
                        return HandleTouchLongPressArpeggioButton(x, y, button);
                    case ButtonType.Dpcm:
                        return HandleTouchLongPressDpcmButton(x, y, button);
                }

                return true;
            }

            return false;
        }

        private bool HandleTouchDownParamSliderButton(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0 && buttons[buttonIdx].type == ButtonType.ParamSlider)
            {
                if (StartMoveSlider(x, y, buttons[buttonIdx], buttonIdx))
                    return true;
            }

            return false;
        }

        private bool IsPositionInButtonIcon(Button button, int buttonRelX, int buttonRelY)
        {
            var iconSize = ScaleCustom(bmpEnvelopesAtlas.GetElementSize(0).Width, bitmapScale);
            var iconRelX = buttonRelX - (buttonIconPosX + (ShowExpandButtons() ? expandButtonPosX + expandButtonSizeX : 0));
            var iconRelY = buttonRelY - buttonIconPosY;

            if (iconRelX < 0 || iconRelX > iconSize ||
                iconRelY < 0 || iconRelY > iconSize)
            {
                return false;
            }

            return true;
        }

        private bool HandleTouchDownDragInstrument(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];
                if (button.instrument != null && buttonIdx == highlightedButtonIdx && subButtonType != SubButtonType.Expand)
                {
                    if (subButtonType == SubButtonType.Max && !IsPositionInButtonIcon(button, buttonRelX, buttonRelY))
                        return false;

                    envelopeDragIdx = subButtonType < SubButtonType.EnvelopeMax ? (int)subButtonType : -1;
                    draggedInstrument = button.instrument;
                    StartCaptureOperation(x, y, CaptureOperation.DragInstrument, buttonIdx, buttonRelX, buttonRelY);
                    return true;
                }
            }

            return false;
        }

        private bool HandleTouchDownDragSong(int x, int y)
        {
            var buttonIdx = GetButtonAtCoord(x, y, out var subButtonType, out var buttonRelX, out var buttonRelY);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];
                if (button.song != null && buttonIdx == highlightedButtonIdx && subButtonType == SubButtonType.Max && IsPositionInButtonIcon(button, buttonRelX, buttonRelY))
                {
                    App.SelectedSong = button.song;
                    StartCaptureOperation(x, y, CaptureOperation.DragSong, buttonIdx);
                    draggedSong = button.song;
                    return true;
                }
            }

            return false;
        }

        private bool HandleTouchDownPan(int x, int y)
        {
            StartCaptureOperation(x, y, CaptureOperation.MobilePan);
            return true;
        }

        protected override void OnTouchDown(int x, int y)
        {
            flingVelY = 0;

            if (HandleTouchDownParamSliderButton(x, y)) goto Handled;
            if (HandleTouchDownDragInstrument(x, y)) goto Handled;
            if (HandleTouchDownDragSong(x, y)) goto Handled;
            if (HandleTouchDownPan(x, y)) goto Handled;
            return;

         Handled:
            MarkDirty();
        }

        protected override void OnTouchClick(int x, int y)
        {
            if (captureOperation != CaptureOperation.None)
                return;

            HandleTouchClickButtons(x, y);
        }

        protected override void OnTouchLongPress(int x, int y)
        {
            AbortCaptureOperation();

            if (HandleTouchLongPressButtons(x, y)) goto Handled;

            return;

        Handled:
            MarkDirty();
        }

        protected override void OnTouchMove(int x, int y)
        {
            UpdateCursor();
            UpdateCaptureOperation(x, y);
            UpdateToolTip(x, y);

            mouseLastX = x;
            mouseLastY = y;
        }

        protected override void OnTouchUp(int x, int y)
        {
            EndCaptureOperation(x, y);
        }

        protected override void OnTouchFling(int x, int y, float velX, float velY)
        {
            if (canFling)
            {
                EndCaptureOperation(x, y);
                flingVelY = velY;
            }
        }

        private void TickFling(float delta)
        {
            if (flingVelY != 0.0f)
            {
                var deltaPixel = (int)Math.Round(flingVelY * delta);
                if (deltaPixel != 0 && DoScroll(deltaPixel))
                    flingVelY *= (float)Math.Exp(delta * -4.5f);
                else
                    flingVelY = 0.0f;
            }
        }

        public void Tick(float delta)
        {
            TickFling(delta);
            UpdateCaptureOperation(mouseLastX, mouseLastY, true);
        }
        
        // Project properties.
        public readonly static string ExpansionAudioTooltip       = "Expansion audio chip(s) to use. This will add extra audio channels and disable any PAL support.";
        public readonly static string ExpansionNumChannelsTooltip = "Namco 163 audio supports between 1 and 8 channels. As you add more channels the audio quality will deteriorate. Only available when the 'Namco 163' expansion is enabled.";
        public readonly static string TempoModeTooltip            = "FamiStudio tempo gives you precise control to every frame, has good PAL/NTSC conversion support and is the recommended way to use FamiStudio. FamiTracker tempo behaves like FamiTracker with speed/tempo settings. Use only if you have very specific compatibility needs as support is limited and it will not yield the best FamiStudio experience.";
        public readonly static string AuthoringMachineTooltip     = "For use with FamiStudio tempo. Defines the machine on which the music is edited. Playback to the other space will be approximate, but still good.";

        // Song properties.
        public readonly static string SongLengthTooltip = "Number of patterns in the song.";

        private void EditProjectProperties(Point pt)
        {
            var project = App.Project;

            var numExpansions = ExpansionType.End - ExpansionType.Start + 1;
            var expNames = new string[numExpansions];
            var expBools = new bool[numExpansions];
            for (int i = ExpansionType.Start; i <= ExpansionType.End; i++)
            {
                expNames[i - ExpansionType.Start] = ExpansionType.Names[i];
                expBools[i - ExpansionType.Start] = project.UsesExpansionAudio(i);
            }

            var dlg = new PropertyDialog("Project Properties", PointToScreen(pt), 360, true);
            dlg.Properties.ShowWarnings = true;
            dlg.Properties.AddTextBox("Title :", project.Name, 31); // 0
            dlg.Properties.AddTextBox("Author :", project.Author, 31); // 1
            dlg.Properties.AddTextBox("Copyright :", project.Copyright, 31); // 2
            dlg.Properties.AddDropDownList("Tempo Mode :", TempoType.Names, TempoType.Names[project.TempoMode], TempoModeTooltip); // 3
            dlg.Properties.AddDropDownList("Authoring Machine :", MachineType.NamesNoDual, MachineType.NamesNoDual[project.PalMode ? MachineType.PAL : MachineType.NTSC], AuthoringMachineTooltip); // 4
            dlg.Properties.AddNumericUpDown("N163 Channels :", project.ExpansionNumN163Channels, 1, 8, ExpansionNumChannelsTooltip); // 5 (Namco)
            dlg.Properties.AddCheckBoxList("Expansion Audio :", expNames, expBools, ExpansionAudioTooltip, 150); // 6
            dlg.Properties.SetPropertyEnabled(4, project.UsesFamiStudioTempo && !project.UsesAnyExpansionAudio);
            dlg.Properties.SetPropertyEnabled(5, project.UsesExpansionAudio(ExpansionType.N163));
            dlg.Properties.PropertyChanged += ProjectProperties_PropertyChanged;
            UpdateProjectPropertiesWarnings(dlg.Properties);
            dlg.Properties.Build();

            dlg.ShowDialogAsync(ParentForm, (r) =>
            {
                if (r == DialogResult.OK)
                {
                    var selectedExpansions = dlg.Properties.GetPropertyValue<bool[]>(6);
                    var expansionMask = 0;
                    var expansionRemoved = false;

                    for (int i = 0; i < selectedExpansions.Length; i++)
                    {
                        var selected = selectedExpansions[i];
                        expansionMask |= (selected ? 1 : 0) << i;

                        if (project.UsesExpansionAudio(i + ExpansionType.Start) && !selected)
                            expansionRemoved = true;
                    }

                    var tempoMode = TempoType.GetValueForName(dlg.Properties.GetPropertyValue<string>(3));
                    var palAuthoring = MachineType.GetValueForName(dlg.Properties.GetPropertyValue<string>(4)) == 1;
                    var numChannels = dlg.Properties.GetPropertyValue<int>(5);

                    var changedTempoMode = tempoMode != project.TempoMode;
                    var changedExpansion = expansionMask != project.ExpansionAudioMask;
                    var changedNumChannels = numChannels != project.ExpansionNumN163Channels;
                    var changedAuthoringMachine = palAuthoring != project.PalMode;

                    var transFlags = TransactionFlags.None;

                    if (changedAuthoringMachine || changedExpansion || changedNumChannels)
                        transFlags = TransactionFlags.ReinitializeAudio;
                    else if (changedTempoMode)
                        transFlags = TransactionFlags.StopAudio;

                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, transFlags);

                    project.Name = dlg.Properties.GetPropertyValue<string>(0);
                    project.Author = dlg.Properties.GetPropertyValue<string>(1);
                    project.Copyright = dlg.Properties.GetPropertyValue<string>(2);

                    if (changedExpansion || changedNumChannels)
                    {
                        var numExpansionsSelected = 0;

                        for (int i = 0; i < selectedExpansions.Length; i++)
                        {
                            if (selectedExpansions[i])
                                numExpansionsSelected++;
                        }

                        if (numExpansionsSelected > 1 && ((expansionMask & ExpansionType.EPSMMask) != 0))
                        {
                            if (PlatformUtils.IsDesktop)
                                PlatformUtils.MessageBox($"EPSM is not supported with other expansions enabled", "Incompatible expansions", MessageBoxButtons.OK);
                            else
                                PlatformUtils.ShowToast($"EPSM is not supported with other expansions enabled, expansion change was ignored.");
                        }
                        else if (!expansionRemoved || PlatformUtils.IsMobile || expansionRemoved && PlatformUtils.MessageBox($"Remove an expansion will delete all instruments and channels using it, continue?", "Change expansion audio", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            App.SelectedInstrument = project.Instruments.Count > 0 ? project.Instruments[0] : null;
                            project.SetExpansionAudioMask(expansionMask, numChannels);
                            ProjectModified?.Invoke();
                            Reset();
                        }
                    }

                    if (changedTempoMode)
                    {
                        if (tempoMode == TempoType.FamiStudio)
                        {
                            if (!project.AreSongsEmpty && PlatformUtils.IsDesktop)
                                PlatformUtils.MessageBox($"Converting from FamiTracker to FamiStudio tempo is extremely crude right now. It will ignore all speed changes and assume a tempo of 150. It is very likely that the songs will need a lot of manual corrections after.", "Change tempo mode", MessageBoxButtons.OK);
                            project.ConvertToFamiStudioTempo();
                        }
                        else if (tempoMode == TempoType.FamiTracker)
                        {
                            if (!project.AreSongsEmpty && PlatformUtils.IsDesktop)
                                PlatformUtils.MessageBox($"Converting from FamiStudio to FamiTracker tempo will simply set the speed to 1 and tempo to 150. It will not try to merge notes or do anything sophisticated.", "Change tempo mode", MessageBoxButtons.OK);
                            project.ConvertToFamiTrackerTempo(project.AreSongsEmpty);
                        }

                        ProjectModified?.Invoke();
                        Reset();
                    }

                    if (changedAuthoringMachine && project.UsesFamiStudioTempo)
                    {
                        project.PalMode = palAuthoring;
                        App.PalPlayback = palAuthoring;
                    }

                    if (PlatformUtils.IsMobile && expansionRemoved)
                    {
                        PlatformUtils.ShowToast("All channels and instruments related to the removed expansion(s) were deleted.");
                    }

                    App.UndoRedoManager.EndTransaction();
                    RefreshButtons();
                }
            });
        }

        private void UpdateProjectPropertiesWarnings(PropertyPage props)
        {
            var selectedExpansions = props.GetPropertyValue<bool[]>(6);
            var numExpansionsSelected = 0;

            for (int i = 0; i < selectedExpansions.Length; i++)
            {
                if (selectedExpansions[i])
                    numExpansionsSelected++;
            }

            if (selectedExpansions[ExpansionType.EPSM - 1] && numExpansionsSelected > 1)
                props.SetPropertyWarning(6, CommentType.Error, "EPSM is unsupported with other expansions enabled.");
            else if (numExpansionsSelected > 1)
                props.SetPropertyWarning(6, CommentType.Warning, "Using multiple expansions will prevent you from exporting to FamiTracker.");
            else
                props.SetPropertyWarning(6, CommentType.Good, "");
        }

        private void ProjectProperties_PropertyChanged(PropertyPage props, int propIdx, int rowIdx, int colIdx, object value)
        {
            var selectedExpansions = props.GetPropertyValue<bool[]>(6);
            var anyExpansionsSelected = false;
            var n163Selected = false;

            for (int i = 0; i < selectedExpansions.Length; i++)
            {
                if (selectedExpansions[i])
                {
                    if (i + ExpansionType.Start == ExpansionType.N163)
                        n163Selected = true;
                    anyExpansionsSelected = true;
                }
            }

            if (propIdx == 6) // Expansion
            {
                props.SetPropertyEnabled(5, n163Selected);
                props.SetPropertyEnabled(4, props.GetPropertyValue<string>(3) == "FamiStudio" && !anyExpansionsSelected);

                if (anyExpansionsSelected)
                    props.SetDropDownListIndex(4, 0);
                else
                    props.SetDropDownListIndex(4, App.Project.PalMode ? 1 : 0);
            }
            else if (propIdx == 3) // Tempo Mode
            {
                props.SetPropertyEnabled(4, (string)value == TempoType.Names[TempoType.FamiStudio] && !anyExpansionsSelected);
            }

            UpdateProjectPropertiesWarnings(props);
        }

        private void EditSongProperties(Point pt, Song song)
        {
            var dlg = new PropertyDialog("Song Properties", PointToScreen(pt), 320, true); 

            var tempoProperties = new TempoProperties(dlg.Properties, song);

            dlg.Properties.AddColoredTextBox(song.Name, song.Color); // 0
            dlg.Properties.AddColorPicker(song.Color); // 1
            dlg.Properties.AddNumericUpDown("Song Length :", song.Length, 1, Song.MaxLength, SongLengthTooltip); // 2
            tempoProperties.AddProperties();
            dlg.Properties.Build();

            dlg.ShowDialogAsync(ParentForm, (r) =>
            {
                if (r == DialogResult.OK)
                {
                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples, TransactionFlags.StopAudio);
                    App.SeekSong(0);

                    var newName = dlg.Properties.GetPropertyValue<string>(0);

                    if (App.Project.RenameSong(song, newName))
                    {
                        song.Color = dlg.Properties.GetPropertyValue<System.Drawing.Color>(1);
                        song.SetLength(dlg.Properties.GetPropertyValue<int>(2));

                        tempoProperties.ApplyAsync(false, () =>
                        {
                            SongModified?.Invoke(song);
                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons(false);
                        });
                    }
                    else
                    {
                        App.UndoRedoManager.AbortTransaction();
                        PlatformUtils.Beep();
                        MarkDirty();
                    }
                }
            });
        }

        private void EditInstrumentProperties(Point pt, Instrument instrument)
        {
            var dlg = new PropertyDialog("Instrument Properties", PointToScreen(pt), 240, true, pt.Y > Height / 2);
            dlg.Properties.AddColoredTextBox(instrument.Name, instrument.Color); // 0
            dlg.Properties.AddColorPicker(instrument.Color); // 1
            dlg.Properties.Build();

            dlg.ShowDialogAsync(ParentForm, (r) =>
            {
                if (r == DialogResult.OK)
                {
                    var newName = dlg.Properties.GetPropertyValue<string>(0);

                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);

                    if (App.Project.RenameInstrument(instrument, newName))
                    {
                        instrument.Color = dlg.Properties.GetPropertyValue<Color>(1);
                        InstrumentColorChanged?.Invoke(instrument);
                        RefreshButtons();
                        App.UndoRedoManager.EndTransaction();
                    }
                    else
                    {
                        App.UndoRedoManager.AbortTransaction();
                        PlatformUtils.Beep();
                    }
                }
            });
        }

        private void EditArpeggioProperties(Point pt, Arpeggio arpeggio)
        {
            var dlg = new PropertyDialog("Arpeggio Properties", PointToScreen(pt), 240, true, pt.Y > Height / 2);
            dlg.Properties.AddColoredTextBox(arpeggio.Name, arpeggio.Color); // 0
            dlg.Properties.AddColorPicker(arpeggio.Color); // 1
            dlg.Properties.Build();

            dlg.ShowDialogAsync(ParentForm, (r) =>
            {
                if (r == DialogResult.OK)
                {
                    var newName = dlg.Properties.GetPropertyValue<string>(0);

                    App.UndoRedoManager.BeginTransaction(TransactionScope.ProjectNoDPCMSamples);

                    if (App.Project.RenameArpeggio(arpeggio, newName))
                    {
                        arpeggio.Color = dlg.Properties.GetPropertyValue<System.Drawing.Color>(1);
                        ArpeggioColorChanged?.Invoke(arpeggio);
                        RefreshButtons();
                        App.UndoRedoManager.EndTransaction();
                    }
                    else
                    {
                        App.UndoRedoManager.AbortTransaction();
                        PlatformUtils.Beep();
                    }
                }
            });
        }

        private void EditDPCMSampleProperties(Point pt, DPCMSample sample)
        {
            var dlg = new PropertyDialog("DPCM Sample Properties", PointToScreen(pt), 240, true, pt.Y > Height / 2);
            dlg.Properties.AddColoredTextBox(sample.Name, sample.Color); // 0
            dlg.Properties.AddColorPicker(sample.Color); // 1
            dlg.Properties.Build();

            dlg.ShowDialogAsync(ParentForm, (r) =>
            {
                var newName = dlg.Properties.GetPropertyValue<string>(0);

                App.UndoRedoManager.BeginTransaction(TransactionScope.DPCMSample, sample.Id);

                if (App.Project.RenameSample(sample, newName))
                {
                    sample.Color = dlg.Properties.GetPropertyValue<System.Drawing.Color>(1);
                    DPCMSampleColorChanged?.Invoke(sample);
                    RefreshButtons();
                    App.UndoRedoManager.EndTransaction();
                }
                else
                {
                    App.UndoRedoManager.AbortTransaction();
                    PlatformUtils.Beep();
                }
            });
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType);

            if (buttonIdx >= 0)
            {
                if (captureOperation != CaptureOperation.None)
                    AbortCaptureOperation();

                var button = buttons[buttonIdx];
                var pt = new Point(e.X, e.Y);   

                if (button.type == ButtonType.ProjectSettings)
                {
                    EditProjectProperties(pt);
                }
                else if (button.type == ButtonType.Song)
                {
                    EditSongProperties(pt, button.song);
                }
                else if (button.type == ButtonType.Instrument && button.instrument != null && subButtonType == SubButtonType.Max)
                {
                    EditInstrumentProperties(pt, button.instrument);
                }
                else if (button.type == ButtonType.Arpeggio && button.arpeggio != null && subButtonType == SubButtonType.Max)
                {
                    EditArpeggioProperties(pt, button.arpeggio);
                }
                else if (button.type == ButtonType.Dpcm && subButtonType == SubButtonType.Max)
                {
                    EditDPCMSampleProperties(pt, button.sample);
                }
#if !FAMISTUDIO_WINDOWS
                else
                {
                    // When pressing multiple times on mac, it creates click -> dbl click -> click -> dbl click sequences which
                    // makes the project explorer feel very sluggish. Interpret dbl click as clicks helps a lot.
                    OnMouseDown(e);
                }
#endif
            }
        }

        public void ValidateIntegrity()
        {
        }

        public void SerializeState(ProjectBuffer buffer)
        {
            buffer.Serialize(ref expandedInstrument);
            buffer.Serialize(ref selectedInstrumentTab);
            buffer.Serialize(ref expandedSample);
            buffer.Serialize(ref scrollY);

            if (buffer.IsReading)
            {
                captureOperation = CaptureOperation.None;
                Capture = false;
                flingVelY = 0.0f;

                ClampScroll();
                RefreshButtons();
            }
        }
    }
}
