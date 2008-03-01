/* 
 *	Copyright (C) 2005-2008 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */
//#define FORM


using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using DirectShowLib;
using DirectShowLib.BDA;
using TvLibrary.Implementations;
using DirectShowLib.SBE;
using TvLibrary.Interfaces;
using TvLibrary.Interfaces.Analyzer;
using TvLibrary.Channels;
using TvLibrary.Implementations.DVB.Structures;
using TvLibrary.Implementations.Helper;
using TvLibrary.Epg;
using TvLibrary.Teletext;
using TvLibrary.Log;
using TvLibrary.Helper;

namespace TvLibrary.Implementations.DVB
{
  public class TvDvbChannel : AVObserverSubChannel, ITeletextCallBack, IPMTCallback, ICACallback, ITvSubChannel, IVideoAudioObserver
  {
    #region MDAPI structs

    #region Struct MDPlug
    // ********************* MDPlug *************************
    [StructLayout(LayoutKind.Sequential)]
    public struct CA_System82
    {
      public ushort CA_Typ;
      public ushort ECM;
      public ushort EMM;
      public uint Provider_Id;
    }
    // ********************* MDPlug *************************

    #endregion

    [StructLayout(LayoutKind.Sequential)]
    public struct TProgram82
    {
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
      public byte[] Name;   // to simulate c++ char Name[30]
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
      public byte[] Provider;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
      public byte[] Country;
      public uint Freq;
      public byte PType;
      public byte Voltage;
      public byte Afc;
      public byte DiSEqC;
      public uint Symbolrate;
      public byte Qam;
      public byte Fec;
      public byte Norm;
      public ushort Tp_id;
      public ushort Video_pid;
      public ushort Audio_pid;
      public ushort TeleText_pid;          // Teletext PID 
      public ushort PMT_pid;
      public ushort PCR_pid;
      public ushort ECM_PID;
      public ushort SID_pid;
      public ushort AC3_pid;
      public byte TVType;           //  == 00 PAL ; 11 == NTSC    
      public byte ServiceTyp;
      public byte CA_ID;
      public ushort Temp_Audio;
      public ushort FilterNr;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
      public byte[] Filters;  // to simulate struct PIDFilters Filters[MAX_PID_IDS];
      public ushort CA_Nr;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
      public CA_System82[] CA_System82;  // to simulate struct TCA_System CA_System[MAX_CA_SYSTEMS];
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
      public byte[] CA_Country;
      public byte Marker;
      public ushort Link_TP;
      public ushort Link_SID;
      public byte PDynamic;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
      public byte[] Extern_Buffer;
    }

    #endregion

    #region interfaces
    [ComVisible(true), ComImport,
    Guid("C3F5AA0D-C475-401B-8FC9-E33FB749CD85"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IChangeChannel
    {
      /// <summary>
      /// Get the file name of media file.
      /// </summary>
      /// <param name="fn">The file name buffer.</param>
      /// <returns></returns>
      /// <remarks>fn should point to a buffer allocated to at least the length of MAX_PATH (=260)</remarks>
      [PreserveSig]
      int ChangeChannel(int frequency, int bandwidth, int polarity, int videopid, int audiopid, int ecmpid, int caid, int providerid);
      int ChangeChannelTP82([In] IntPtr tp82);
      //int ChangeChannel(IntPtr tp82);
    }
    #endregion

    #region variables
    #region local variables
    protected GraphState _graphState = GraphState.Idle;
    protected bool _startTimeShifting = false;
    protected string _timeshiftFileName = "";
    protected DateTime _dateTimeShiftStarted = DateTime.MinValue;

    protected bool _startRecording = false;
    protected string _recordingFileName = "";
    protected DateTime _dateRecordingStarted = DateTime.MinValue;
    protected bool _recordTransportStream = false;
    protected bool _newPMT = false;
    protected bool _newCA = false;
    protected int _pmtVersion;
    protected int _pmtPid = -1;
    protected ChannelInfo _channelInfo;
    protected IChannel _currentChannel;
    protected DVBTeletext _teletextDecoder;

    protected ITsFilter _tsFilterInterface = null;
    protected int _subChannelIndex = -1;
    protected DVBAudioStream _currentAudioStream;
    protected IVbiCallback _teletextCallback = null;
    protected IChangeChannel _changeChannel = null;
    protected TProgram82 _mDPlugTProg82 = new TProgram82();
    protected int _subChannelId;
#if FORM
    protected System.Windows.Forms.Timer _pmtTimer = new System.Windows.Forms.Timer();
#else
    protected System.Timers.Timer _pmtTimer = new System.Timers.Timer();
#endif
    bool _pmtTimerRentrant = false;


    #region teletext
    protected bool _grabTeletext = false;
    protected bool _hasTeletext = false;
    TSHelperTools.TSHeader _packetHeader;
    TSHelperTools _tsHelper;

    private int _pmtLength;
    private byte[] _pmtData;
    #endregion
    #endregion

    #region graph variables
    ConditionalAccess _conditionalAccess;
    IBaseFilter _mdapiFilter;
    IBaseFilter _filterTIF;
    IBaseFilter _filterTsWriter;
    IFilterGraph2 _graphBuilder;
    bool _graphRunning;
    ScanParameters _parameters;
    #endregion
    #endregion

    #region ctor
    /// <summary>
    /// Initializes a new instance of the <see cref="TvDvbChannel"/> class.
    /// </summary>
    public TvDvbChannel()
    {
      _graphState = GraphState.Created;
      _graphRunning = false;
      _mDPlugTProg82.CA_Country = new byte[5];
      _mDPlugTProg82.CA_System82 = new CA_System82[32];
      _mDPlugTProg82.Country = new byte[30];
      _mDPlugTProg82.Extern_Buffer = new byte[16];
      _mDPlugTProg82.Filters = new byte[256];
      _mDPlugTProg82.Name = new byte[30];
      _mDPlugTProg82.Provider = new byte[30];


      _pmtTimer.Enabled = false;
      _pmtTimer.Interval = 100;
#if FORM
      _pmtTimer.Tick += new EventHandler(_pmtTimer_ElapsedForm);
#else
      _pmtTimer.Elapsed += new System.Timers.ElapsedEventHandler(_pmtTimer_Elapsed);
#endif
      _teletextDecoder = new DVBTeletext();
      _packetHeader = new TSHelperTools.TSHeader();
      _tsHelper = new TSHelperTools();
      _channelInfo = new ChannelInfo();
      _pmtPid = -1;
      _parameters = new ScanParameters();
      _subChannelId = 0;
      _timeshiftFileName = "";
      _recordingFileName = "";
      _pmtData = null;
      _pmtLength = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TvDvbChannel"/> class.
    /// </summary>
    /// <param name="graphBuilder">The graph builder.</param>
    /// <param name="ca">The ca.</param>
    /// <param name="mdapiFilter">The mdapi filter.</param>
    /// <param name="tif">The tif filter.</param>
    /// <param name="tsWriter">The ts writer filter.</param>
    public TvDvbChannel(IFilterGraph2 graphBuilder, ref ConditionalAccess ca, IBaseFilter mdapiFilter, IBaseFilter tif, IBaseFilter tsWriter, int subChannelId)
    {
      _graphState = GraphState.Created;
      _graphRunning = false;
      _graphBuilder = graphBuilder;
      _conditionalAccess = ca;
      _mdapiFilter = mdapiFilter;
      _filterTIF = tif;
      _filterTsWriter = tsWriter;
      _mDPlugTProg82.CA_Country = new byte[5];
      _mDPlugTProg82.CA_System82 = new CA_System82[32];
      _mDPlugTProg82.Country = new byte[30];
      _mDPlugTProg82.Extern_Buffer = new byte[16];
      _mDPlugTProg82.Filters = new byte[256];
      _mDPlugTProg82.Name = new byte[30];
      _mDPlugTProg82.Provider = new byte[30];


      _pmtTimer.Enabled = false;
      _pmtTimer.Interval = 100;
#if FORM
      _pmtTimer.Tick += new EventHandler(_pmtTimer_ElapsedForm);
#else
      _pmtTimer.Elapsed += new System.Timers.ElapsedEventHandler(_pmtTimer_Elapsed);
#endif
      _teletextDecoder = new DVBTeletext();
      _packetHeader = new TSHelperTools.TSHeader();
      _tsHelper = new TSHelperTools();
      _channelInfo = new ChannelInfo();
      _pmtPid = -1;
      _changeChannel = (IChangeChannel)_mdapiFilter;

      _subChannelIndex = -1;
      _tsFilterInterface = (ITsFilter)_filterTsWriter;
      _tsFilterInterface.AddChannel(ref _subChannelIndex);
      _parameters = new ScanParameters();
      _subChannelId = subChannelId;

      _conditionalAccess.AddSubChannel(_subChannelId);
      _timeshiftFileName = "";
      _recordingFileName = "";
      _pmtData = null;
      _pmtLength = 0;
    }
    #endregion

    #region public methods

    /// <summary>
    /// Gets the sub channel id.
    /// </summary>
    /// <value>The sub channel id.</value>
    public int SubChannelId
    {
      get
      {
        return _subChannelId;
      }
    }

    #region tuning and graph methods
    /// <summary>
    /// Should be called before tuning to a new channel
    /// resets the state
    /// </summary>
    public void OnBeforeTune()
    {
      Log.Log.WriteFile("subch:{0} OnBeforeTune", _subChannelId);
      if (IsTimeShifting)
      {
        if (_subChannelIndex >= 0)
        {
          _tsFilterInterface.TimeShiftPause(_subChannelIndex, 1);
        }
      }
      _startTimeShifting = false;
      _startRecording = false;
      _channelInfo = new ChannelInfo();
      _pmtTimer.Enabled = false;
      _hasTeletext = false;
      _currentAudioStream = null;
    }

    /// <summary>
    /// Should be called when the graph is tuned to the new channel
    /// resets the state
    /// </summary>
    public void OnAfterTune()
    {
      Log.Log.WriteFile("subch:{0} OnAfterTune", _subChannelId);
      _pmtTimer.Enabled = true;
      ArrayList pids = new ArrayList();
      pids.Add((ushort)0x0);//pat
      pids.Add((ushort)0x11);//sdt
      pids.Add((ushort)0x1fff);//padding stream
      if (_currentChannel != null)
      {
        DVBBaseChannel ch = (DVBBaseChannel)_currentChannel;
        if (ch.PmtPid > 0)
        {
          pids.Add((ushort)ch.PmtPid);//sdt
        }
      }
      //SendHwPids(pids);

      _pmtPid = -1;
      _pmtVersion = -1;
      _newPMT = false;
      _newCA = false;
    }

    /// <summary>
    /// Should be called when the graph is about to start
    /// Resets the state 
    /// If graph is already running, starts the pmt grabber to grab the
    /// pmt for the new channel
    /// </summary>
    public void OnGraphStart()
    {
      Log.Log.WriteFile("subch:{0} OnGraphStart", _subChannelId);
      DateTime dtNow;
      if (_graphBuilder != null)
      {
        FilterState state;
        (_graphBuilder as IMediaControl).GetState(10, out state);
        if (state == FilterState.Running)
        {
          Log.Log.WriteFile("subch:{0} RunGraph: already running", _subChannelId);
          _graphRunning = true;
          //_pmtVersion = -1;
          DVBBaseChannel channel = _currentChannel as DVBBaseChannel;
          if (channel != null)
          {
            if (SetupPmtGrabber(channel.PmtPid, channel.ServiceId))
            {
              dtNow = DateTime.Now;
              while (_pmtVersion < 0 && channel.PmtPid > 0)
              {
                Log.Log.Write("subch:{0} wait for pmt {1:X}", _subChannelId, channel.PmtPid);
                System.Threading.Thread.Sleep(20);
                TimeSpan ts = DateTime.Now - dtNow;
                if (ts.TotalMilliseconds >= (_parameters.TimeOutPMT * 1000)) break;
              }
            }
          }
          return;
        }
      }
      Log.Log.WriteFile("subch{0}:RunGraph", _subChannelId);
      if (_teletextDecoder != null)
        _teletextDecoder.ClearBuffer();
      _pmtPid = -1;
      _pmtVersion = -1;
      _newPMT = false;
      _newCA = false;
    }

    /// <summary>
    /// Should be called when the graph has been started
    /// sets up the pmt grabber to grab the pmt of the channel
    /// </summary>
    public void OnGraphStarted()
    {
      Log.Log.WriteFile("subch:{0} OnGraphStarted", _subChannelId);
      _graphRunning = true;
      _dateTimeShiftStarted = DateTime.MinValue;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      bool result = false;
      if (dvbChannel != null)
      {
        result=SetupPmtGrabber(dvbChannel.PmtPid, dvbChannel.ServiceId);
      }
      _pmtTimer.Enabled = true;
      if (dvbChannel != null && result)
      {
        if (dvbChannel.PmtPid >= 0)
        {
          DateTime dtNow = DateTime.Now;
          while (_pmtVersion < 0)
          {
            Log.Log.Write("subch:{0} wait for pmt:{1:X}", _subChannelId, dvbChannel.PmtPid);
            System.Threading.Thread.Sleep(20);
            TimeSpan ts = DateTime.Now - dtNow;
            if (ts.TotalMilliseconds >= (_parameters.TimeOutPMT * 1000)) break;
          }
        }
      }
    }

    /// <summary>
    /// Should be called when graph is about to stop.
    /// stops any timeshifting/recording on this channel
    /// </summary>
    public void OnGraphStop()
    {
      Log.Log.WriteFile("subch:{0} OnGraphStop", _subChannelId);
      _pmtPid = -1;
      _dateTimeShiftStarted = DateTime.MinValue;
      _dateRecordingStarted = DateTime.MinValue;
      _pmtTimer.Enabled = false;
      _startTimeShifting = false;
      _startRecording = false;
      _pmtVersion = -1;
      _newPMT = false;
      _newCA = false;
      _recordingFileName = "";
      _channelInfo = new ChannelInfo();
      _currentChannel = null;
      _recordTransportStream = false;

      if (_tsFilterInterface != null)
      {
        _tsFilterInterface.RecordStopRecord(_subChannelIndex);
        _tsFilterInterface.TimeShiftStop(_subChannelIndex);
        _graphState = GraphState.Created;
      }
      if (_teletextDecoder != null)
      {
        _teletextDecoder.ClearBuffer();
      }
    }

    /// <summary>
    /// should be called when graph has been stopped
    /// Resets the graph state
    /// </summary>
    public void OnGraphStopped()
    {
      Log.Log.WriteFile("subch:{0} OnGraphStopped", _subChannelId);
      _graphRunning = false;
      _graphState = GraphState.Created;

    }

    /// <summary>
    /// disposes this channel
    /// </summary>
    public void Decompose()
    {
      Log.Log.Info("subch:{0} Decompose()", _subChannelId);
      if (IsRecording)
        StopRecording();
      if (IsTimeShifting)
        StopTimeShifting();
      _timeshiftFileName = "";
      _recordingFileName = "";
      _pmtTimer.Enabled = false;
      _graphRunning = false;
      if (_teletextDecoder != null)
      {
        _teletextDecoder.ClearBuffer();
      }
      if (_tsFilterInterface != null && _subChannelIndex >= 0)
      {
        _tsFilterInterface.DeleteChannel(_subChannelIndex);
        _subChannelIndex = -1;
      }
      _conditionalAccess.FreeSubChannel(_subChannelId);
    }

    /// <summary>
    /// sets the filename used for timeshifting
    /// </summary>
    /// <param name="fileName">timeshifting filename</param>
    public void SetTimeShiftFileName(string fileName)
    {
      if (_channelInfo.pids.Count == 0)
      {
        Log.Log.WriteFile("subch:{0} SetTimeShiftFileName no pmt received. Timeshifting failed", _subChannelId);
        return;
      }
      _timeshiftFileName = fileName;
      Log.Log.WriteFile("subch:{0} SetTimeShiftFileName:{1}", _subChannelId, fileName);
      //int hr;
      if (_tsFilterInterface != null)
      {
        _tsFilterInterface.TimeShiftSetParams(_subChannelIndex, _parameters.MinimumFiles, _parameters.MaximumFiles, _parameters.MaximumFileSize);
        _tsFilterInterface.TimeShiftSetTimeShiftingFileName(_subChannelIndex, fileName);
        _tsFilterInterface.TimeShiftSetMode(_subChannelIndex, TimeShiftingMode.TransportStream);
        Log.Log.WriteFile("subch:{0} SetTimeShiftFileName fill in pids", _subChannelId);
        _startTimeShifting = false;
        SetTimeShiftPids();
        _tsFilterInterface.TimeShiftStart(_subChannelIndex);

        Log.Log.WriteFile("Set video / audio observer");
        _tsFilterInterface.SetVideoAudioObserver(_subChannelIndex, this);

        _graphState = GraphState.TimeShifting;
      }
    }
    #endregion

    #region recording methods

    /// <summary>
    /// Starts recording
    /// </summary>
    /// <param name="transportStream">if set to <c>true</c> [transport stream].</param>
    /// <param name="fileName">filename to which to recording should be saved</param>
    public bool StartRecording(bool transportStream, string fileName)
    {
      Log.Log.WriteFile("subch:{0} StartRecord({1})", _subChannelId, fileName);
      _recordTransportStream = transportStream;
      int hr;
      if (_tsFilterInterface != null)
      {
        if (transportStream)
        {
          _tsFilterInterface.RecordSetMode(_subChannelIndex, TimeShiftingMode.TransportStream);
          Log.Log.WriteFile("subch:{0} record transport stream mode", _subChannelId);
        }
        else
        {
          _tsFilterInterface.RecordSetMode(_subChannelIndex, TimeShiftingMode.ProgramStream);
          Log.Log.WriteFile("subch:{0} record program stream mode", _subChannelId);
        }

        hr = _tsFilterInterface.RecordSetRecordingFileName(_subChannelIndex, fileName);
        if (hr != 0)
        {
          Log.Log.Error("subch:{0} SetRecordingFileName failed:{1:X}", _subChannelId, hr);
        }
        if (_channelInfo.pids.Count == 0)
        {
          Log.Log.WriteFile("subch:{0} StartRecord no pmt received yet", _subChannelId);
          _startRecording = true;
        }
        else
        {
          Log.Log.WriteFile("subch:{0} StartRecording...", _subChannelId);
          SetRecorderPids();
          hr = _tsFilterInterface.RecordStartRecord(_subChannelIndex);
          if (hr != 0)
          {
            Log.Log.Error("subch:{0} StartRecord failed:{1:X}", _subChannelId, hr);
          }
          _graphState = GraphState.Recording;
        }
      }
      _recordingFileName = fileName;
      return true;
    }

    /// <summary>
    /// Stop recording
    /// </summary>
    /// <returns></returns>
    public bool StopRecording()
    {
      if (IsRecording)
      {
        Log.Log.WriteFile("subch:{0} StopRecord()", _subChannelId);

        if (_tsFilterInterface != null)
        {
          _tsFilterInterface.RecordStopRecord(_subChannelIndex);
          if (_timeshiftFileName != "")
            _graphState = GraphState.TimeShifting;
          else
            _graphState = GraphState.Created;
        }
      }
      _startRecording = false;
      _recordTransportStream = false;
      _recordingFileName = "";
      return true;
    }


    /// <summary>
    /// Starts timeshifting. Note card has to be tuned first
    /// </summary>
    /// <param name="fileName">filename used for the timeshiftbuffer</param>
    /// <returns>true if succeeded else false</returns>
    public bool StartTimeShifting(string fileName)
    {
      SetTimeShiftFileName(fileName);
      return (_channelInfo.pids.Count != 0);
    }
    /// <summary>
    /// Stop recording
    /// </summary>
    /// <returns></returns>
    public bool StopTimeShifting()
    {
      if (_timeshiftFileName != "")
      {
        Log.Log.WriteFile("subch:{0} StopTimeshifting()", _subChannelId);
        if (_tsFilterInterface != null)
        {
          _tsFilterInterface.TimeShiftStop(_subChannelIndex);
        }
        _graphState = GraphState.Created;
      }
      _startTimeShifting = false;
      _timeshiftFileName = "";
      return true;
    }
    #endregion

    #region audio streams methods
    /// <summary>
    /// returns the list of available audio streams
    /// </summary>
    public List<IAudioStream> AvailableAudioStreams
    {
      get
      {

        List<IAudioStream> streams = new List<IAudioStream>();
        foreach (PidInfo info in _channelInfo.pids)
        {
          if (info.isAC3Audio)
          {
            DVBAudioStream stream = new DVBAudioStream();
            stream.Language = info.language;
            stream.Pid = info.pid;
            stream.StreamType = AudioStreamType.AC3;
            streams.Add(stream);
          }
          else if (info.isAudio)
          {
            DVBAudioStream stream = new DVBAudioStream();
            stream.Language = info.language;
            stream.Pid = info.pid;
            if (info.IsMpeg1Audio)
              stream.StreamType = AudioStreamType.Mpeg1;
            if (info.IsMpeg2Audio)
              stream.StreamType = AudioStreamType.Mpeg2;
            if (info.IsAACAudio)
              stream.StreamType = AudioStreamType.AAC;
            else
              stream.StreamType = AudioStreamType.LATMAAC;
            streams.Add(stream);
          }
        }
        return streams;
      }
    }

    /// <summary>
    /// get/set the current selected audio stream
    /// </summary>
    public IAudioStream CurrentAudioStream
    {
      get
      {
        return _currentAudioStream;
      }
      set
      {

        List<IAudioStream> streams = AvailableAudioStreams;
        DVBAudioStream audioStream = (DVBAudioStream)value;
        if (_tsFilterInterface != null)
        {
          _tsFilterInterface.AnalyzerSetAudioPid(_subChannelIndex, audioStream.Pid);
        }
        _currentAudioStream = audioStream;
        _pmtVersion = -1;
        bool updatePids;
        SendPmtToCam(out updatePids);
      }
    }

    public int GetCurrentVideoStream
    {
      get
      {
        if (_channelInfo == null) return -1;
        foreach (PidInfo info in _channelInfo.pids)
        {
          if (info.isVideo)
          {
            return info.stream_type;
          }
        }
        return -1;
      }
    }


    #endregion
    #endregion

    #region public properties

    #region recording/timeshifting properties
    /// <summary>
    /// returns true if we record in transport stream mode
    /// false we record in program stream mode
    /// </summary>
    /// <value>true for transport stream, false for program stream.</value>
    public bool IsRecordingTransportStream
    {
      get
      {
        if (_channelInfo == null) return false;
        foreach (PidInfo info in _channelInfo.pids)
        {
          if (info.isVideo)
          {
            if (info.IsH264Video || info.IsMpeg4Video) return true;
          }
        }
        return false;
      }
    }

    /// <summary>
    /// returns true if we timeshift in transport stream mode
    /// false we timeshift in program stream mode
    /// </summary>
    /// <value>true for transport stream, false for program stream.</value>
    public bool IsTimeshiftingTransportStream
    {
      get
      {
        if (IsTimeShifting) return true;
        return false;
      }
    }

    /// <summary>
    /// gets the current filename used for timeshifting
    /// </summary>
    public string TimeShiftFileName
    {
      get
      {
        return _timeshiftFileName;
      }
    }

    /// <summary>
    /// returns the date/time when timeshifting has been started for the card specified
    /// </summary>
    /// <returns>DateTime containg the date/time when timeshifting was started</returns>
    public DateTime StartOfTimeShift
    {
      get
      {
        return _dateTimeShiftStarted;
      }
    }

    /// <summary>
    /// returns the date/time when recording has been started for the card specified
    /// </summary>
    /// <returns>DateTime containg the date/time when recording was started</returns>
    public DateTime RecordingStarted
    {
      get
      {
        return _dateRecordingStarted;
      }
    }

    /// <summary>
    /// gets the current filename used for recording
    /// </summary>
    /// <value></value>
    public string RecordingFileName
    {
      get
      {
        return _recordingFileName;
      }
    }

    /// <summary>
    /// returns true if card is currently recording
    /// </summary>
    /// <value>true if recording otherwise false</value>
    public bool IsRecording
    {
      get
      {
        return (_recordingFileName.Length > 0);
      }
    }

    /// <summary>
    /// 
    /// returns true if card is currently timeshifting
    /// </summary>
    /// <value></value>
    public bool IsTimeShifting
    {
      get
      {
        return (_timeshiftFileName.Length > 0);
      }
    }
    #endregion

    #region general properties

    /// <summary>
    /// Gets or sets the parameters.
    /// </summary>
    /// <value>The parameters.</value>
    public ScanParameters Parameters
    {
      get
      {
        return _parameters;
      }
      set
      {
        _parameters = value;
      }
    }
    /// <summary>
    /// Gets or sets the current channel.
    /// </summary>
    /// <value>The current channel.</value>
    public IChannel CurrentChannel
    {
      get
      {
        return _currentChannel;
      }
      set
      {
        _currentChannel = value;
      }
    }


    /// <summary>
    /// Returns true when unscrambled audio/video is received otherwise false
    /// </summary>
    /// <returns>true of false</returns>
    public bool IsReceivingAudioVideo
    {
      get
      {
        if (_graphRunning == false) return false;
        if (_tsFilterInterface == null) return false;
        if (_currentChannel == null) return false;

        int audioEncrypted = 0;
        int videoEncrypted = 0;
        _tsFilterInterface.AnalyzerIsAudioEncrypted(_subChannelIndex, out audioEncrypted);
        if (_currentChannel.IsTv)
        {
          _tsFilterInterface.AnalyzerIsVideoEncrypted(_subChannelIndex, out videoEncrypted);
        }
        return ((audioEncrypted == 0) && (videoEncrypted == 0));
      }
    }
    #endregion

    #region teletext properties
    /// <summary>
    /// Turn on/off teletext grabbing
    /// </summary>
    public bool GrabTeletext
    {
      get
      {
        return _grabTeletext;
      }
      set
      {
        _grabTeletext = value;
        if (_grabTeletext)
        {
          int teletextPid = -1;
          foreach (PidInfo pidInfo in _channelInfo.pids)
          {
            if (pidInfo.isTeletext)
            {
              teletextPid = pidInfo.pid;
              break;
            }
          }

          if (teletextPid == -1)
          {
            Log.Log.Info("subch: stop grabbing teletext");
            _tsFilterInterface.TTxStop(_subChannelIndex);
            _grabTeletext = false;
            return;
          }
          Log.Log.Info("subch: start grabbing teletext");
          _tsFilterInterface.TTxSetCallBack(_subChannelIndex, this);
          _tsFilterInterface.TTxSetTeletextPid(_subChannelIndex, teletextPid);
          _tsFilterInterface.TTxStart(_subChannelIndex);
        }
        else
        {
          Log.Log.Info("subch: stop grabbing teletext");
          _tsFilterInterface.TTxStop(_subChannelIndex);
        }
      }
    }

    /// <summary>
    /// Property which returns true when the current channel contains teletext
    /// </summary>
    public bool HasTeletext
    {
      get
      {
        return (_hasTeletext);
      }
    }

    /// <summary>
    /// returns the ITeletext interface which can be used for
    /// getting teletext pages
    /// </summary>
    public ITeletext TeletextDecoder
    {
      get
      {
        return _teletextDecoder;
      }
    }
    #endregion

    #endregion

    #region pidmapping

    /// <summary>
    /// Instructs the ts analyzer filter to start grabbing the PMT
    /// </summary>
    /// <param name="pmtPid">pid of the PMT</param>
    /// <param name="serviceId">The service id.</param>
    protected bool SetupPmtGrabber(int pmtPid, int serviceId)
    {
      Log.Log.Info("subch:{0} SetupPmtGrabber:pid {1:X} sid:{2:X}", _subChannelId, pmtPid, serviceId);
      if (pmtPid < 0) return false;
      if (pmtPid == _pmtPid) return false;
      _pmtVersion = -1;
      _pmtPid = pmtPid;

      if (_conditionalAccess != null)
        _conditionalAccess.OnRunGraph(serviceId);

      Log.Log.Write("subch:{0} set pmt grabber pmt:{1:X} sid:{2:X}", _subChannelId, pmtPid, serviceId);
      _tsFilterInterface.PmtSetCallBack(_subChannelIndex, this);
      _tsFilterInterface.PmtSetPmtPid(_subChannelIndex, pmtPid, serviceId);
      if (_mdapiFilter != null)
      {
        Log.Log.Write("subch:{0} set ca grabber ", _subChannelId);
        _tsFilterInterface.CaSetCallBack(_subChannelIndex, this);
        _tsFilterInterface.CaReset(_subChannelIndex);

      }

      return true;
    }

    /// <summary>
    /// maps the correct pids to the TsFileSink filter and teletext pins
    /// </summary>
    /// <param name="info"></param>
    protected void SetMpegPidMapping(ChannelInfo info)
    {
      if (info == null) return;
      try
      {
        Log.Log.WriteFile("subch:{0} SetMpegPidMapping", _subChannelId);

        ArrayList hwPids = new ArrayList();
        hwPids.Add((ushort)0x0);//PAT
        hwPids.Add((ushort)0x1);//CAT
        hwPids.Add((ushort)0x10);//NIT
        hwPids.Add((ushort)0x11);//SDT

        Log.Log.WriteFile("subch:{0}  pid:{1:X} pcr", _subChannelId, info.pcr_pid);
        Log.Log.WriteFile("subch:{0}  pid:{1:X} pmt", _subChannelId, info.network_pmt_PID);

        if (info.pids != null)
        {
          foreach (PidInfo pidInfo in info.pids)
          {
            Log.Log.WriteFile("subch:{0}  {1}", _subChannelId, pidInfo.ToString());
            if (pidInfo.pid == 0 || pidInfo.pid > 0x1fff) continue;
            if (pidInfo.isTeletext)
            {
              Log.Log.WriteFile("subch:{0}    map {1}", _subChannelId, pidInfo);
              if (GrabTeletext)
              {
                _tsFilterInterface.TTxSetTeletextPid(_subChannelIndex, pidInfo.pid);
              }
              hwPids.Add((ushort)pidInfo.pid);
              _hasTeletext = true;
            }
            if (pidInfo.isAC3Audio || pidInfo.isAudio)
            {
              if (_currentAudioStream == null || pidInfo.isAC3Audio)
              {
                _currentAudioStream = new DVBAudioStream();
                _currentAudioStream.Pid = pidInfo.pid;
                _currentAudioStream.Language = pidInfo.language;
                if (pidInfo.IsMpeg1Audio)
                  _currentAudioStream.StreamType = AudioStreamType.Mpeg1;
                else if (pidInfo.IsMpeg2Audio)
                  _currentAudioStream.StreamType = AudioStreamType.Mpeg2;
                if (pidInfo.isAC3Audio)
                  _currentAudioStream.StreamType = AudioStreamType.AC3;
                if (pidInfo.IsAACAudio)
                  _currentAudioStream.StreamType = AudioStreamType.AAC;
                if (pidInfo.IsLATMAACAudio)
                  _currentAudioStream.StreamType = AudioStreamType.LATMAAC;
              }

              if (_currentAudioStream.Pid == pidInfo.pid)
              {
                Log.Log.WriteFile("subch:{0}    map {1}", _subChannelId, pidInfo);
                _tsFilterInterface.AnalyzerSetAudioPid(_subChannelIndex,pidInfo.pid);
              }
              hwPids.Add((ushort)pidInfo.pid);
            }

            if (pidInfo.isVideo)
            {
              Log.Log.WriteFile("subch:{0}    map {1}", _subChannelId, pidInfo);
              hwPids.Add((ushort)pidInfo.pid);
              _tsFilterInterface.AnalyzerSetVideoPid(_subChannelIndex,pidInfo.pid);
              if (info.pcr_pid > 0 && info.pcr_pid != pidInfo.pid)
              {
                hwPids.Add((ushort)info.pcr_pid);
              }
            }
          }
        }
        if (info.network_pmt_PID >= 0 && ((DVBBaseChannel)_currentChannel).ServiceId >= 0)
        {
          hwPids.Add((ushort)info.network_pmt_PID);
          // SendHwPids(hwPids);
        }

        if (_startTimeShifting)
        {
          _startTimeShifting = false;
          _tsFilterInterface.TimeShiftReset(_subChannelIndex);
          SetTimeShiftPids();
          _tsFilterInterface.TimeShiftStart(_subChannelIndex);

          Log.Log.WriteFile("Set video / audio observer");
          _tsFilterInterface.SetVideoAudioObserver(_subChannelIndex, this);

          _graphState = GraphState.TimeShifting;
        }
        if (_startRecording)
        {
          _startRecording = false;
          SetRecorderPids();

          int hr = _tsFilterInterface.RecordStartRecord(_subChannelIndex);
          if (hr != 0)
          {
            Log.Log.Error("subch:[0} StartRecord failed:{1:X}", _subChannelId, hr);
          }
          _graphState = GraphState.Recording;
        }
        else if (IsTimeShifting)
        {
          SetTimeShiftPids();
        }
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
    }

    /// <summary>
    /// Sets the pids for the timeshifter
    /// </summary>
    void SetTimeShiftPids()
    {
      //Log.Log.WriteFile("SetTimeShiftPids new DLL");
      if (_channelInfo == null) return;
      if (_channelInfo.pids.Count == 0) return;
      if (_currentChannel == null) return;
      //if (_currentAudioStream == null) return;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      if (dvbChannel == null) return;


      _tsFilterInterface.TimeShiftPause(_subChannelIndex, 1);
      _tsFilterInterface.TimeShiftSetPmtPid(_subChannelIndex, dvbChannel.PmtPid,dvbChannel.ServiceId,_pmtData,_pmtLength);
     
      //_linkageScannerEnabled = (layer.GetSetting("linkageScannerEnabled", "no").Value == "yes");
      
      _tsFilterInterface.TimeShiftPause(_subChannelIndex, 0);
      _dateTimeShiftStarted = DateTime.Now;
    }

    /// <summary>
    /// Sets the pids for the recorder
    /// </summary>
    void SetRecorderPids()
    {
      Log.Log.WriteFile("SetRecorderPids");
      if (_channelInfo == null) return;
      if (_channelInfo.pids.Count == 0) return;
      if (_currentChannel == null) return;
      if (_currentAudioStream == null) return;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      if (dvbChannel == null) return;

      if (_recordTransportStream)
      {
        if (dvbChannel.PmtPid > 0)
        {
          _tsFilterInterface.RecordSetPmtPid(_subChannelIndex, dvbChannel.PmtPid, dvbChannel.ServiceId, _pmtData, _pmtLength);
        }
      }
      else
      {
        if (dvbChannel.PmtPid > 0)
        {
          _tsFilterInterface.RecordSetPmtPid(_subChannelIndex, dvbChannel.PmtPid, dvbChannel.ServiceId, _pmtData, _pmtLength);
        }
      }
      _dateRecordingStarted = DateTime.Now;
    }

    /// <summary>
    /// Decodes the PMT and sends the PMT to cam.
    /// </summary>
    protected bool SendPmtToCam(out bool updatePids)
    {
      lock (this)
      {
        updatePids = false;
        if (_mdapiFilter != null)
        {
          // speeds up channel changing for FTA channels
          DVBBaseChannel chan = _currentChannel as DVBBaseChannel;
          if (chan != null)
          {
            //HACK: Currently Premiere Direkt Feeds (nid=133) have the free_ca flag in SDT set to true (means not scrambled), so we have to override this
            if ((!chan.FreeToAir) || (chan.NetworkId==133 && !chan.Provider.Equals("BetaDigital")))
            {
              if (_newCA == false)
              {
                Log.Log.Info("subch:{0} SendPmt:wait for ca", _subChannelId);
                return false;//cat not received yet
              }
            }
          }
        }
        DVBBaseChannel channel = _currentChannel as DVBBaseChannel;
        if (channel == null)
        {
          Log.Log.Info("subch:{0} SendPmt:no channel set", _subChannelId);
          return true;
        }
        IntPtr pmtMem = Marshal.AllocCoTaskMem(4096);// max. size for pmt
        IntPtr catMem = Marshal.AllocCoTaskMem(4096);// max. size for cat
        try
        {
          _pmtLength = _tsFilterInterface.PmtGetPMTData(_subChannelIndex,pmtMem);
          if (_pmtLength > 6)
          {
            _pmtData = new byte[_pmtLength];
            int version = -1;
            Marshal.Copy(pmtMem, _pmtData, 0, _pmtLength);
            version = ((_pmtData[5] >> 1) & 0x1F);
            int pmtProgramNumber = (_pmtData[3] << 8) + _pmtData[4];
            Log.Log.Info("subch:{0} SendPmt:{1:X} {2:X} {3:X} {4:X}", _subChannelId, pmtProgramNumber, channel.ServiceId, _pmtVersion, version);
            if (pmtProgramNumber == channel.ServiceId)
            {
              if (_pmtVersion != version)
              {
                _channelInfo = new ChannelInfo();
                _channelInfo.DecodePmt(_pmtData);
                _channelInfo.network_pmt_PID = channel.PmtPid;
                if (channel.PcrPid <= 0)
                {
                  channel.PcrPid = _channelInfo.pcr_pid;
                }
                _channelInfo.pcr_pid = channel.PcrPid;
                // update any service scrambled / unscambled changes
                if (_channelInfo.scrambled = channel.FreeToAir)
                {
                  channel.FreeToAir = !_channelInfo.scrambled;
                }
                if ((_mdapiFilter != null) && (_newCA))
                {
                  try
                  {
                    int catLength = _tsFilterInterface.CaGetCaData(_subChannelIndex, catMem);
                    if (catLength > 0)
                    {
                      byte[] cat = new byte[catLength];
                      Marshal.Copy(catMem, cat, 0, catLength);
                      _channelInfo.DecodeCat(cat, catLength);
                    }
                  }
                  catch (Exception ex)
                  {
                    Log.Log.Write(ex); ;
                  }
                }
                
                updatePids = true;
                Log.Log.WriteFile("subch:{0} SendPMT version:{1} len:{2} {3}", _subChannelId, version, _pmtLength, _channelInfo.caPMT.ProgramNumber);
                if (_conditionalAccess != null)
                {
                  int audioPid = -1;
                  if (_currentAudioStream != null)
                  {
                    audioPid = _currentAudioStream.Pid;
                  }

                  if (_conditionalAccess.SendPMT(_subChannelId, CamType.Default, (DVBBaseChannel)CurrentChannel, _pmtData, _pmtLength, audioPid))
                  {
                    _pmtVersion = version;
                    Log.Log.WriteFile("subch:{0} cam flags:{1}", _subChannelId, _conditionalAccess.IsCamReady());
                    _pmtTimer.Interval = 100;
                    return true;
                  }
                  else
                  {
                    //cam is not ready yet
                    Log.Log.WriteFile("subch:{0} SendPmt failed cam flags:{1}", _subChannelId, _conditionalAccess.IsCamReady());
                    _pmtVersion = -1;
                    _pmtTimer.Interval = 3000;
                    return false;
                  }
                }
                else
                {
                  Log.Log.Info("subch:{0} No cam in use", _subChannelId);
                }
                _pmtTimer.Interval = 100;
                _pmtVersion = version;

                return true;
              }
              else
              {
                //already received this pmt
                return true;
              }
            }
          }
        }
        catch (Exception ex)
        {
          Log.Log.Write(ex);
        }
        finally
        {
          Marshal.FreeCoTaskMem(pmtMem);
          Marshal.FreeCoTaskMem(catMem);
        }
      }
      return false;
    }
    #endregion

    #region pmt timer callbacks
    /// <summary>
    /// timer callback. This method checks if a new PMT has been received
    /// and ifso updates the various pid mappings and updates the CI interface
    /// with the new PMT
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    void _pmtTimer_ElapsedForm(object sender, EventArgs args)
    {
      _pmtTimer_Elapsed(null, null);
    }

    /// <summary>
    /// Handles the Elapsed event of the _pmtTimer control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="T:System.Timers.ElapsedEventArgs"/> instance containing the event data.</param>
    void _pmtTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
      try
      {
        if (_graphRunning == false) return;
        if (_pmtTimerRentrant) return;
        _pmtTimerRentrant = true;

        if (_newPMT)
        {
          bool updatePids;
          if (SendPmtToCam(out updatePids))
          {
            _newPMT = false;
            if (updatePids)
            {
              if (_channelInfo != null)
              {
                SetMpegPidMapping(_channelInfo);
                SetChannel2MDPlug();
              }
              Log.Log.Info("subch:{0} stop tif", _subChannelId);
              if (_filterTIF != null)
                _filterTIF.Stop();
            }
          }
        }
      }
      catch (Exception ex)
      {
        Log.Log.WriteFile("subch:{0}", ex.Message);
        Log.Log.WriteFile("subch:{0}", ex.Source);
        Log.Log.WriteFile("subch::{0}", ex.StackTrace);
      }
      finally
      {
        _pmtTimerRentrant = false;
      }
    }
    #endregion

    #region tswriter callback handlers

    #region ITeletextCallBack Members

    public IVbiCallback TeletextCallback
    {
      get
      {
        return _teletextCallback;
      }
      set
      {
        _teletextCallback = value;
      }
    }
    /// <summary>
    /// callback from the TsWriter filter when it received a new teletext packets
    /// </summary>
    /// <param name="data">teletext data</param>
    /// <param name="packetCount">number of packets in data</param>
    /// <returns></returns>
    public int OnTeletextReceived(IntPtr data, short packetCount)
    {
      try
      {
        if (_teletextCallback != null)
        {
          _teletextCallback.OnVbiData(data, packetCount, false);
        }
        for (int i = 0; i < packetCount; ++i)
        {
          IntPtr packetPtr = new IntPtr(data.ToInt32() + i * 188);
          ProcessPacket(packetPtr);
        }

      }
      catch (Exception ex)
      {
        Log.Log.WriteFile(ex.ToString());
      }
      return 0;
    }
    /// <summary>
    /// processes a single transport packet
    /// Called from BufferCB
    /// </summary>
    /// <param name="ptr">pointer to the transport packet</param>
    public void ProcessPacket(IntPtr ptr)
    {
      if (ptr == IntPtr.Zero) return;

      _packetHeader = _tsHelper.GetHeader((IntPtr)ptr);
      if (_packetHeader.SyncByte != 0x47)
      {
        Log.Log.Write("packet sync error");
        return;
      }
      if (_packetHeader.TransportError == true)
      {
        Log.Log.Write("packet transport error");
        return;
      }
      // teletext
      if (_grabTeletext)
      {
        if (_teletextDecoder != null)
        {
          _teletextDecoder.SaveData((IntPtr)ptr);
        }
      }
    }
    #endregion

    #region ICaCallback Members

    /// <summary>
    /// Called when tswriter.ax has received a new ca section
    /// </summary>
    /// <returns></returns>
    public int OnCaReceived()
    {
      _newCA = true;
      Log.Log.WriteFile("subch:OnCaReceived()");

      return 0;
    }

    #endregion

    #region IVideoAudioObserver
    /// <summary>
    /// Called when tswriter.ax has seen the video / audio data for the first time
    /// </summary>
    /// <returns></returns>
    public int OnNotify(PidType pidType)
    {
      try
      {
        Log.Log.WriteFile("PID seen - type = {0}", pidType);
        OnAudioVideoEvent(pidType);
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
      return 0;
    }
    #endregion


    #region IPMTCallback Members
    /// <summary>
    /// Called when tswriter.ax has received a new pmt
    /// </summary>
    /// <returns></returns>
    public int OnPMTReceived()
    {
      try
      {
        Log.Log.WriteFile("subch:{0} OnPMTReceived() {1}", _subChannelId, _graphRunning);
        _newPMT = false;
        if (_graphRunning == false) return 0;
        bool updatePids;
        if (SendPmtToCam(out updatePids))
        {
          if (updatePids)
          {
            if (_channelInfo != null)
            {
              SetMpegPidMapping(_channelInfo);
              SetChannel2MDPlug();
            }
            Log.Log.Info("subch:{0} stop tif", _subChannelId);
            if (_filterTIF != null)
              _filterTIF.Stop();
          }
        }
        else
        {
          _newPMT = true;
        }
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
      return 0;
    }


#if notused
    /// <summary>
    /// Sends the PMT to cam.
    /// </summary>
    protected bool SendPmtToCam(byte[] pmt, out bool updatePids)
    {
      lock (this)
      {
        updatePids = false;
        if ((_currentChannel as ATSCChannel) != null) return true;
        DVBBaseChannel channel = _currentChannel as DVBBaseChannel;
        if (channel == null) return true;
        try
        {
          if (pmt.Length > 6)
          {
            int version = -1;
            version = ((pmt[5] >> 1) & 0x1F);
            int pmtProgramNumber = (pmt[3] << 8) + pmt[4];
            if (pmtProgramNumber == channel.ServiceId)
            {
              if (_pmtVersion != version)
              {
                _channelInfo = new ChannelInfo();
                _channelInfo.DecodePmt(pmt);
                _channelInfo.network_pmt_PID = channel.PmtPid;
                _channelInfo.pcr_pid = channel.PcrPid;

                int pmtLength = pmt.Length;
                updatePids = true;
                Log.Log.WriteFile("dvb:SendPMT version:{0} len:{1} {2}", version, pmtLength, _channelInfo.caPMT.ProgramNumber);
                if (_conditionalAccess != null)
                {
                  int audioPid = -1;
                  if (_currentAudioStream != null)
                  {
                    audioPid = _currentAudioStream.Pid;
                  }

                  if (_conditionalAccess.SendPMT(CamType.Default, (DVBBaseChannel)Channel, pmt, pmtLength, audioPid))
                  {
                    _pmtVersion = version;
                    Log.Log.WriteFile("dvb:cam flags:{0}", _conditionalAccess.IsCamReady());
                    _pmtTimer.Interval = 100;
                    return true;
                  }
                  else
                  {
                    //cam is not ready yet
                    Log.Log.WriteFile("dvb:SendPmt failed cam flags:{0}", _conditionalAccess.IsCamReady());
                    _pmtVersion = -1;
                    _pmtTimer.Interval = 3000;
                    return false;
                  }
                }
                _pmtTimer.Interval = 100;
                _pmtVersion = version;

                return true;
              }
              else
              {
                //already received this pmt
                return true;
              }
            }
          }
        }
        catch (Exception ex)
        {
          Log.Log.Write(ex);
        }
        finally
        {
        }
      }
      return false;
    }I mean.

#endif
    #endregion
    #endregion

    #region MDAPI
    /// <summary>
    /// Sends the current channel to the mdapi filter
    /// </summary>
    private void SetChannel2MDPlug()
    {

      int Index;
      int end_Index = 0;
      //is mdapi installed?
      if (_mdapiFilter == null) return; //nop, then return

      //did we already receive the pmt?
      if (_channelInfo == null) return; //nop, then return
      if (_channelInfo.caPMT == null) return;      
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      if (dvbChannel == null) //not a DVB channel??
        return;

      if (_mDPlugTProg82.SID_pid == (ushort)dvbChannel.ServiceId)
        return; //already tuned to this service?

      //set channel name
      if (dvbChannel.Name != null)
      {
        end_Index = _mDPlugTProg82.Name.GetLength(0) - 1;

        if (dvbChannel.Name.Length < end_Index)
        {
          end_Index = dvbChannel.Name.Length;
        }
        for (Index = 0; Index < end_Index; ++Index)
        {
          _mDPlugTProg82.Name[Index] = (byte)dvbChannel.Name[Index];
        }
      }
      else
        end_Index = 0;
      _mDPlugTProg82.Name[end_Index] = 0;

      //set provide name
      if (dvbChannel.Provider != null)
      {
        end_Index = _mDPlugTProg82.Provider.GetLength(0) - 1;
        if (dvbChannel.Provider.Length < end_Index)
          end_Index = dvbChannel.Provider.Length;
        for (Index = 0; Index < end_Index; ++Index)
        {
          _mDPlugTProg82.Provider[Index] = (byte)dvbChannel.Provider[Index];
        }
      }
      else
        end_Index = 0;
      _mDPlugTProg82.Provider[end_Index] = 0;

      //public byte[] Country;
      _mDPlugTProg82.Freq = (uint)dvbChannel.Frequency;
      //public byte PType = (byte);
      _mDPlugTProg82.Afc = (byte)68;
      //_mDPlugTProg82.DiSEqC = (byte)dvbChannel.DisEqc;
      //_mDPlugTProg82.Symbolrate = (uint)dvbChannel.SymbolRate;
      //public byte Qam;

      _mDPlugTProg82.Fec = 0;
      //public byte Norm;
      _mDPlugTProg82.Tp_id = (ushort)dvbChannel.TransportId;
      _mDPlugTProg82.SID_pid = (ushort)dvbChannel.ServiceId;
      _mDPlugTProg82.PMT_pid = (ushort)dvbChannel.PmtPid;
      _mDPlugTProg82.PCR_pid = (ushort)dvbChannel.PcrPid;
      if (_channelInfo != null)
      {
        foreach (PidInfo pid in _channelInfo.pids)
        {
          if (pid.isVideo)
            _mDPlugTProg82.Video_pid = (ushort)pid.pid;
          if (pid.isAudio)
            _mDPlugTProg82.Audio_pid = (ushort)pid.pid;
          if (pid.isTeletext)
            _mDPlugTProg82.TeleText_pid = (ushort)pid.pid;
          if (pid.isAC3Audio)
            _mDPlugTProg82.AC3_pid = (ushort)pid.pid;
        }
        if (_currentChannel.IsTv)
          _mDPlugTProg82.ServiceTyp = (byte)1;
        else
          _mDPlugTProg82.ServiceTyp = (byte)2;
      }
      //public byte TVType;           //  == 00 PAL ; 11 == NTSC    
      //public ushort Temp_Audio;
      _mDPlugTProg82.FilterNr = (ushort)0; //to test
      //public byte[] Filters;  // to simulate struct PIDFilters Filters[MAX_PID_IDS];
      //public byte[] CA_Country;
      //public byte Marker;
      //public ushort Link_TP;
      //public ushort Link_SID;
      _mDPlugTProg82.PDynamic = (byte)0; //to test
      //public byte[] Extern_Buffer;
      if (_channelInfo.caPMT != null)
      {
        //get all EMM's (from CAT (pid 0x1))
        List<ECMEMM> emmList = _channelInfo.caPMT.GetEMM();
        if (emmList.Count <= 0) return;
        for (int i = 0; i < emmList.Count; ++i)
        {
          Log.Log.Info("EMM #{0} CA:0x{1:X} EMM:0x{2:X} ID:0x{3:X}",
                i, emmList[i].CaId, emmList[i].Pid, emmList[i].ProviderId);
        }

        //get all ECM's for this service
        List<ECMEMM> ecmList = _channelInfo.caPMT.GetECM();
        for (int i = 0; i < ecmList.Count; ++i)
        {
          Log.Log.Info("ECM #{0} CA:0x{1:X} ECM:0x{2:X} ID:0x{3:X}",
                i, ecmList[i].CaId, ecmList[i].Pid, ecmList[i].ProviderId);
        }


        _mDPlugTProg82.CA_Nr = (ushort)ecmList.Count;
        int count = 0;
        for (int x = 0; x < ecmList.Count; ++x)
        {
          _mDPlugTProg82.CA_System82[x].CA_Typ = (ushort)ecmList[x].CaId;
          _mDPlugTProg82.CA_System82[x].ECM = (ushort)ecmList[x].Pid;
          _mDPlugTProg82.CA_System82[x].EMM = 0;
          _mDPlugTProg82.CA_System82[x].Provider_Id = (uint)ecmList[x].ProviderId;
          count++;
        }

        for (int i = 0; i < emmList.Count; ++i)
        {
          bool found = false;
          for (int j = 0; j < count; ++j)
          {
            if (emmList[i].ProviderId == _mDPlugTProg82.CA_System82[j].Provider_Id && emmList[i].CaId == _mDPlugTProg82.CA_System82[j].CA_Typ)
            {
              found = true;
              _mDPlugTProg82.CA_System82[j].EMM = (ushort)emmList[i].Pid;
              break;
            }
          }
          if (!found)
          {
            _mDPlugTProg82.CA_System82[count].CA_Typ = (ushort)emmList[i].CaId;
            _mDPlugTProg82.CA_System82[count].ECM = 0;
            _mDPlugTProg82.CA_System82[count].EMM = (ushort)emmList[i].Pid;
            _mDPlugTProg82.CA_System82[count].Provider_Id = (uint)emmList[i].ProviderId;
            count++;
          }
        }

        _mDPlugTProg82.CA_ID = (byte)0;
        _mDPlugTProg82.CA_Nr = (ushort)count;
        _mDPlugTProg82.ECM_PID = _mDPlugTProg82.CA_System82[0].ECM;

        for (int i = 0; i < count; ++i)
        {
          Log.Log.Info("#{0} CA:0x{1:X} ECM:0x{2:X} EMM:0x{3:X}  provider:0x{4:X}",
                  i,
                  _mDPlugTProg82.CA_System82[i].CA_Typ,
                  _mDPlugTProg82.CA_System82[i].ECM,
                  _mDPlugTProg82.CA_System82[i].EMM,
                  _mDPlugTProg82.CA_System82[i].Provider_Id);
        }
      }
      //ca types:
      //0xb00 : conax
      //0x100 : seca
      //0x500 : Viaccess
      //0x622 : irdeto
      //0x1801: Nagravision
      /* C CINEMA PREMIERE 11856000 V 27500 1:1072:8206 video:165 audio:101 pcr:165 pmt:1285 emm:0 ecm:6664
       *      CA  ECM     EMM     ProviderId
              500 1a08     0      008100
              500 1a06     0      008300
              100 067e     0      0085
              100 067d     0      0084
              100 067c     0      0080
              ecm:6664 ServiceTyp: 1 Volt: 0 Typ: V AFC: 68 fec: 0 srate: 158572

        
        NED 1 12515000 H 22000 53:1105:4011 video:517 audio:88 pcr:8190 pmt:2111 emm:310 ecm:1383
              622  567   136      0
              100  643    B6      6a
              100  661    B6      6C
              d02    0  1389      0
              ecm:567 ServiceTyp: 1 Volt: 0 Typ: H AFC: 68 fec: 0 srate: 153072
                        0 1 2 3 4  5  6 7  8  9 10
              emm len:F 9 D 1 0 E0 B6 2 E0 B7 0 6A E0 B9 0 6C       
                100  b6  6a
                100  b6  6c
              
                
        PLAYBOYTV 10876000 V 22000 1:1060:30603 video:173 audio:132 pcr:173 pmt:1027 emm:193 ecm:1564
              100   61c    c1      4101
              1801  72E    c5         0
              1881  0      91         0
              1882  0      c6         0
              ecm:567 ServiceTyp: 1 Volt: 0 Typ: H AFC: 68 fec: 0 srate: 153072

       * 
      */
      IntPtr lparam = Marshal.AllocHGlobal(Marshal.SizeOf(_mDPlugTProg82));
      Marshal.StructureToPtr(_mDPlugTProg82, lparam, true);
      try
      {
        if (_changeChannel != null)
        {
          Log.Log.Info("Send channel change to MDAPI filter Ca_Id:0x{0:X} CA_Nr:{1:X} ECM_PID:{2:X}",
               _mDPlugTProg82.CA_ID,
               _mDPlugTProg82.CA_Nr,
               _mDPlugTProg82.ECM_PID);
          _changeChannel.ChangeChannelTP82(lparam);
        }
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
      Marshal.FreeHGlobal(lparam);
    }
    #endregion

  }
}
