﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace FamiStudio
{
    public enum LoopMode
    {
        LoopPoint,
        Song,
        Pattern,
        None,
        Max
    };

    public interface IPlayerInterface
    {
        void NotifyInstrumentLoaded(Instrument instrument, int channelTypeMask);
        void NotifyRegisterWrite(int apuIndex, int reg, int data);
    }

    public class BasePlayer : IPlayerInterface
    {
        protected int apuIndex;
        protected NesApu.DmcReadDelegate dmcCallback;
        protected int sampleRate;
        protected int loopCount = 0;
        protected int maxLoopCount = -1;
        protected int frameNumber = 0;
        protected int playbackRate = 1; // 1 = normal, 2 = 1/2, 4 = 1/4, etc.
        protected int playbackRateCounter = 1;
        protected int minSelectedPattern = -1;
        protected int maxSelectedPattern = -1;
        protected int numPlayedPatterns = 0;
        protected bool famitrackerTempo = true;
        protected bool palPlayback = false;
        protected bool seeking = false;
        protected bool beat = false;
        protected bool stereo = false;
        protected int  tndMode = NesApu.TND_MODE_SINGLE;
        protected int  beatIndex = -1;
        protected Song song;
        protected ChannelState[] channelStates;
        protected LoopMode loopMode = LoopMode.Song;
        protected volatile int channelMask = -1;
        protected volatile int playPosition = 0;
        protected NoteLocation playLocation = new NoteLocation(0, 0);
        protected NesApu.NesRegisterValues registerValues = new NesApu.NesRegisterValues();

        // Only used by FamiTracker tempo.
        protected int famitrackerTempoCounter = 0;
        protected int famitrackerSpeed = 6;

        // Only used by FamiStudio tempo.
        protected GrooveIterator grooveIterator;

        // Only used by FamiStudio tempo when doing adapted playback (NTSC -> PAL or vice-versa).
        protected byte[] tempoEnvelope;
        protected int tempoEnvelopeIndex;
        protected int tempoEnvelopeCounter;

        protected BasePlayer(int apu, bool stereo, int rate = 44100)
        {
            this.apuIndex = apu;
            this.sampleRate = rate;
            this.dmcCallback = new NesApu.DmcReadDelegate(NesApu.DmcReadCallback);
            this.stereo = stereo;
        }

        public virtual void Shutdown()
        {
        }

        public int ChannelMask
        {
            get { return channelMask; }
            set { channelMask = value; }
        }

        public LoopMode Loop
        {
            get { return loopMode; }
            set { loopMode = value; }
        }

        public int PlayPosition
        {
            get { return Math.Max(0, playPosition); }
            set { playPosition = value; }
        }

        public int PlayRate
        {
            get { return playbackRate; }
            set
            {
                Debug.Assert(value == 1 || value == 2 || value == 4);
                playbackRate = value;
            }
        }
        
        public void SetSelectionRange(int min, int max)
        {
            minSelectedPattern = min;
            maxSelectedPattern = max;
        }

        // Returns the number of frames to run (0, 1 or 2)
        protected int UpdateFamiStudioTempo()
        {
            if (famitrackerTempo || song.Project.PalMode == palPlayback)
            {
                return 1;
            }
            else
            {
                if (--tempoEnvelopeCounter <= 0)
                {
                    tempoEnvelopeIndex++;

                    if (tempoEnvelope[tempoEnvelopeIndex] == 0x80)
                        tempoEnvelopeIndex = 1;

                    tempoEnvelopeCounter = tempoEnvelope[tempoEnvelopeIndex];

#if FALSE //DEBUG
                    if (song.Project.PalMode)
                        Debug.WriteLine("*** Will do nothing for 1 frame!");
                    else
                        Debug.WriteLine("*** Will run 2 frames!"); 
#endif

                    // A NTSC song playing on PAL will sometimes need to run 2 frames to keep up.
                    // A PAL song playing on NTSC will sometimes need to do nothing for 1 frame to avoid going too fast.
                    return palPlayback ? 2 : 0;
                }
                else
                {
                    return 1;
                }
            }
        }

        private void ResetFamiStudioTempo()
        {
            if (!famitrackerTempo)
            {
                var newGroove = song.GetPatternGroove(playLocation.PatternIndex);
                var newGroovePadMode = song.GetPatternGroovePaddingMode(playLocation.PatternIndex);

                FamiStudioTempoUtils.ValidateGroove(newGroove);

                grooveIterator = new GrooveIterator(newGroove, newGroovePadMode);

                tempoEnvelope = FamiStudioTempoUtils.GetTempoEnvelope(newGroove, newGroovePadMode, song.Project.PalMode);
                tempoEnvelopeCounter = tempoEnvelope[0];
                tempoEnvelopeIndex = 0;
            }
        }

        protected bool ShouldAdvanceSong()
        {
            if (famitrackerTempo)
            {
                return famitrackerTempoCounter <= 0;
            }
            else
            {
                return !grooveIterator.IsPadFrame;
            }
        }

        protected void UpdateTempo()
        {
            if (famitrackerTempo)
            {
                // Tempo/speed logic straight from Famitracker.
                var tempoDecrement = (song.FamitrackerTempo * 24) / famitrackerSpeed;
                var tempoRemainder = (song.FamitrackerTempo * 24) % famitrackerSpeed;

                if (famitrackerTempoCounter <= 0)
                {
                    int ticksPerSec = palPlayback ? 50 : 60;
                    famitrackerTempoCounter += (60 * ticksPerSec) - tempoRemainder;
                }

                famitrackerTempoCounter -= tempoDecrement;
            }
            else
            {
                grooveIterator.Advance();
            }
        }

        protected void AdvanceChannels()
        {
            foreach (var channel in channelStates)
            {
                channel.Advance(song, playLocation, ref famitrackerSpeed);
            }
        }

        protected void UpdateChannels()
        {
            foreach (var channel in channelStates)
            {
                channel.Update();
            }
        }

        protected void EnableChannelType(int channelType, bool enable)
        {
            var exp = ChannelType.GetExpansionTypeForChannelType(channelType);
            var idx = ChannelType.GetExpansionChannelIndexForChannelType(channelType);

            NesApu.EnableChannel(apuIndex, exp, idx, enable ? 1 : 0);
        }

        protected void UpdateChannelsMuting()
        {
            for (int i = 0; i < channelStates.Length; i++)
            {
                var state = channelStates[i];

                EnableChannelType(state.InnerChannelType, (channelMask & (1 << i)) != 0);
            }
        }

        public bool BeginPlaySong(Song s, bool pal, int startNote)
        {
            song = s;
            famitrackerTempo = song.UsesFamiTrackerTempo;
            famitrackerSpeed = song.FamitrackerSpeed;
            palPlayback = pal;
            playPosition = startNote;
            playLocation = new NoteLocation(0, 0);
            frameNumber = 0;
            famitrackerTempoCounter = 0;
            channelStates = CreateChannelStates(this, song.Project, apuIndex, song.Project.ExpansionNumN163Channels, palPlayback);

            Debug.Assert(song.Project.OutputsStereoAudio == stereo);

            NesApu.InitAndReset(apuIndex, sampleRate, palPlayback, tndMode, song.Project.ExpansionAudioMask, song.Project.ExpansionNumN163Channels, dmcCallback);

            ResetFamiStudioTempo();
            UpdateChannelsMuting();

            //Debug.WriteLine($"START SEEKING!!"); 

            if (startNote != 0)
            {
                seeking = true;
                NesApu.StartSeeking(apuIndex);

                AdvanceChannels();
                UpdateChannels();
                UpdateTempo();

                while (playLocation.ToAbsoluteNoteIndex(song) < startNote - 1)
                {
                    if (!PlaySongFrameInternal(true))
                        break;
                }

                NesApu.StopSeeking(apuIndex);
                seeking = false;
            }
            else
            {
                AdvanceChannels();
                UpdateChannels();
                UpdateTempo();
            }

            playPosition = playLocation.ToAbsoluteNoteIndex(song);
            UpdateBeat();

            EndFrame();

            return true;
        }

        protected bool PlaybackRateShouldSkipFrame(bool seeking)
        {
            if (seeking)
                return false;

            if (--playbackRateCounter > 0)
                return true;

            Debug.Assert(playbackRateCounter == 0);
            playbackRateCounter = playbackRate;
            return false;
        }

        protected bool PlaySongFrameInternal(bool seeking)
        {
            ClearBeat();

            //Debug.WriteLine($"PlaySongFrameInternal {playPosition}!");
            //Debug.WriteLine($"PlaySongFrameInternal {song.GetPatternStartNote(playPattern) + playNote}!");

            if (PlaybackRateShouldSkipFrame(seeking))
                return true;

            // Increment before for register listeners to get correct frame number.
            frameNumber++;

            int numFramesToRun = UpdateFamiStudioTempo();

            for (int i = 0; i < numFramesToRun; i++)
            {
                if (ShouldAdvanceSong())
                {
                    //Debug.WriteLine($"  Seeking Frame {song.GetPatternStartNote(playPattern) + playNote}!");

                    if (!AdvanceSong(song.Length, seeking ? LoopMode.None : loopMode))
                        return false;

                    AdvanceChannels();
                }

                UpdateChannels();
                UpdateTempo();

#if DEBUG
                if (i > 0)
                {
                    var noteLength = song.GetPatternNoteLength(playLocation.PatternIndex);
                    if ((playLocation.NoteIndex % noteLength) == 0 && noteLength != 1)
                        Debug.WriteLine("*********** INVALID SKIPPED NOTE!");
                }
#endif
            }

            if (!seeking)
                playPosition = playLocation.ToAbsoluteNoteIndex(song);

            return true;
        }

        public bool PlaySongFrame()
        {
            if (!PlaySongFrameInternal(false))
                return false;

            UpdateChannelsMuting();
            EndFrame();

            return true;
        }

        protected bool AdvanceSong(int songLength, LoopMode loopMode)
        {
            bool advancedPattern = false;

            if (++playLocation.NoteIndex >= song.GetPatternLength(playLocation.PatternIndex))
            {
                playLocation.NoteIndex = 0;

                if (loopMode != LoopMode.Pattern)
                {
                    playLocation.PatternIndex++;
                    advancedPattern = true;
                }
                else
                {
                    // Make sure the selection is valid, updated on another thread, so could be 
                    // sketchy.
                    var minPatternIdx = minSelectedPattern;
                    var maxPatternIdx = maxSelectedPattern;

                    if (minPatternIdx >= 0 && 
                        maxPatternIdx >= 0 &&
                        maxPatternIdx >= minPatternIdx &&
                        minPatternIdx <  song.Length)
                    {
                        if (playLocation.PatternIndex + 1 > maxPatternIdx)
                        {
                            playLocation.PatternIndex = minPatternIdx;
                        }
                        else
                        {
                            playLocation.PatternIndex++;
                            advancedPattern = true;
                        }
                    }
                }
            }

            if (playLocation.PatternIndex >= songLength)
            {
                loopCount++;

                if (maxLoopCount > 0 && loopCount >= maxLoopCount)
                {
                    return false;
                }

                if (loopMode == LoopMode.LoopPoint) // This loop mode is actually unused.
                {
                    if (song.LoopPoint >= 0)
                    {
                        playLocation.PatternIndex = song.LoopPoint;
                        playLocation.NoteIndex = 0;
                        advancedPattern = true;
                    }
                    else 
                    {
                        return false;
                    }
                }
                else if (loopMode == LoopMode.Song)
                {
                    playLocation.PatternIndex = Math.Max(0, song.LoopPoint);
                    playLocation.NoteIndex = 0;
                    advancedPattern = true;
                }
                else if (loopMode == LoopMode.None)
                {
                    return false;
                }
            }

            if (advancedPattern)
            {
                numPlayedPatterns++;
                ResetFamiStudioTempo();
            }

            UpdateBeat();

            return true;
        }

        private void ClearBeat()
        {
            beat = false;
        }

        private void UpdateBeat()
        {
            if (!seeking)
            {
                var beatLength = song.GetPatternBeatLength(playLocation.PatternIndex);

                beat = playLocation.NoteIndex % beatLength == 0;
                beatIndex = playLocation.NoteIndex / beatLength;
            }
            else
            {
                beat = false;
                beatIndex = -1;
            }
        }

        private ChannelState CreateChannelState(int apuIdx, int channelType, int expNumChannels, bool pal)
        {
            switch (channelType)
            {
                case ChannelType.Square1:
                case ChannelType.Square2:
                    return new ChannelStateSquare(this, apuIdx, channelType, pal);
                case ChannelType.Triangle:
                    return new ChannelStateTriangle(this, apuIdx, channelType, pal);
                case ChannelType.Noise:
                    return new ChannelStateNoise(this, apuIdx, channelType, pal);
                case ChannelType.Dpcm:
                    return new ChannelStateDpcm(this, apuIdx, channelType, pal);
                case ChannelType.Vrc6Square1:
                case ChannelType.Vrc6Square2:
                    return new ChannelStateVrc6Square(this, apuIdx, channelType);
                case ChannelType.Vrc6Saw:
                    return new ChannelStateVrc6Saw(this, apuIdx, channelType);
                case ChannelType.Vrc7Fm1:
                case ChannelType.Vrc7Fm2:
                case ChannelType.Vrc7Fm3:
                case ChannelType.Vrc7Fm4:
                case ChannelType.Vrc7Fm5:
                case ChannelType.Vrc7Fm6:
                    return new ChannelStateVrc7(this, apuIdx, channelType);
                case ChannelType.FdsWave:
                    return new ChannelStateFds(this, apuIdx, channelType);
                case ChannelType.Mmc5Square1:
                case ChannelType.Mmc5Square2:
                    return new ChannelStateMmc5Square(this, apuIdx, channelType);
                case ChannelType.N163Wave1:
                case ChannelType.N163Wave2:
                case ChannelType.N163Wave3:
                case ChannelType.N163Wave4:
                case ChannelType.N163Wave5:
                case ChannelType.N163Wave6:
                case ChannelType.N163Wave7:
                case ChannelType.N163Wave8:
                    return new ChannelStateN163(this, apuIdx, channelType, expNumChannels, pal);
                case ChannelType.S5BSquare1:
                case ChannelType.S5BSquare2:
                case ChannelType.S5BSquare3:
                    return new ChannelStateS5B(this, apuIdx, channelType, pal);
                case ChannelType.EPSMSquare1:
                case ChannelType.EPSMSquare2:
                case ChannelType.EPSMSquare3:
                    return new ChannelStateEPSMSquare(this, apuIdx, channelType, pal);
                case ChannelType.EPSMFm1:
                case ChannelType.EPSMFm2:
                case ChannelType.EPSMFm3:
                case ChannelType.EPSMFm4:
                case ChannelType.EPSMFm5:
                case ChannelType.EPSMFm6:
                    return new ChannelStateEPSMFm(this, apuIdx, channelType, pal);
                case ChannelType.EPSMrythm1:
                case ChannelType.EPSMrythm2:
                case ChannelType.EPSMrythm3:
                case ChannelType.EPSMrythm4:
                case ChannelType.EPSMrythm5:
                case ChannelType.EPSMrythm6:
                    return new ChannelStateEPSMRythm(this, apuIdx, channelType, pal);
            }

            Debug.Assert(false);
            return null;
        }

        protected ChannelState[] CreateChannelStates(IPlayerInterface player, Project project, int apuIdx, int expNumChannels, bool pal)
        {
            var channelCount = project.GetActiveChannelCount();
            var states = new ChannelState[channelCount];

            int idx = 0;
            for (int i = 0; i < ChannelType.Count; i++)
            {
                if (project.IsChannelActive(i))
                {
                    var state = CreateChannelState(apuIdx, i, expNumChannels, pal);
                    states[idx++] = state;
                }
            }

            return states;
        }
        
        protected virtual unsafe short[] EndFrame()
        {
            NesApu.EndFrame(apuIndex);

            int numTotalSamples = NesApu.SamplesAvailable(apuIndex);
            short[] samples = new short[numTotalSamples * (stereo ? 2 : 1)];

            fixed (short* ptr = &samples[0])
            {
                NesApu.ReadSamples(apuIndex, new IntPtr(ptr), numTotalSamples);
            }

            ReadBackRegisterValues();

            return samples;
        }

        public void ForceInstrumentsReload()
        {
            if (channelStates != null)
            {
                foreach (var channelState in channelStates)
                    channelState.ForceInstrumentReload();
            }
        }

        public void NotifyInstrumentLoaded(Instrument instrument, int channelTypeMask)
        {
            foreach (var channelState in channelStates)
            {
                if (((1 << channelState.InnerChannelType) & channelTypeMask) != 0)
                {
                    channelState.IntrumentLoadedNotify(instrument);
                }
            }
        }

        public virtual void NotifyRegisterWrite(int apuIndex, int reg, int data)
        {
        }

        protected void ReadBackRegisterValues()
        {
            if (Settings.ShowRegisterViewer)
            {
                lock (registerValues)
                {
                    registerValues.ReadRegisterValues(apuIndex);

                    // Read some additionnal information that we may need for the
                    // register viewer, such as instrument colors, etc.
                    for (int i = 0; i < channelStates.Length; i++)
                    {
                        var state = channelStates[i];
                        var note = state.CurrentNote;
                        var instrument = note != null ? note.Instrument : null;

                        registerValues.InstrumentColors[state.InnerChannelType] = instrument != null ? instrument.Color : System.Drawing.Color.Transparent;

                        if (state.InnerChannelType >= ChannelType.N163Wave1 &&
                            state.InnerChannelType <= ChannelType.N163Wave8)
                        {
                            var idx = state.InnerChannelType - ChannelType.N163Wave1;
                            registerValues.N163InstrumentRanges[idx].Position = instrument != null ? instrument.N163WavePos : (byte)0;
                            registerValues.N163InstrumentRanges[idx].Size = instrument != null ? instrument.N163WaveSize : (byte)0;
                        }
                    }
                }
            }
        }

        public void GetRegisterValues(NesApu.NesRegisterValues values)
        {
            lock (registerValues)
            {
                registerValues.CopyTo(values);
            }
        }
    };
}
