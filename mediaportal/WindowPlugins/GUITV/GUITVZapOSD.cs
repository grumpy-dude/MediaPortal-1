using System;
using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.TV.Recording;
using MediaPortal.TV.Database;
using MediaPortal.Util;

namespace MediaPortal.GUI.TV
{
  /// <summary>
  /// 
  /// </summary>
  /// 

  public class GUITVZAPOSD: GUIWindow
  {
		[SkinControlAttribute(35)]			  protected GUILabelControl lblCurrentChannel=null;
		[SkinControlAttribute(36)]			  protected GUITextControl	lblOnTvNow=null;
		[SkinControlAttribute(37)]			  protected GUITextControl	lblOnTvNext=null;
		[SkinControlAttribute(100)]			  protected GUILabelControl lblCurrentTime=null;
		[SkinControlAttribute(39)]				protected GUIImage				imgRecIcon=null;
		[SkinControlAttribute(10)]				protected GUIImage				imgTvChannelLogo=null;


	  bool m_bNeedRefresh=false;
    DateTime m_dateTime=DateTime.Now;
	  
    ArrayList m_channels = new ArrayList();

    public GUITVZAPOSD()
    {
		
			GetID=(int)GUIWindow.Window.WINDOW_TVZAPOSD;
			TVDatabase.GetChannels(ref m_channels);
    }

    public override bool Init()
    {
      bool bResult=Load (GUIGraphicsContext.Skin+@"\tvZAPOSD.xml");
      GetID=(int)GUIWindow.Window.WINDOW_TVZAPOSD;
      return bResult;
    }


    public override bool SupportsDelayedLoad
    {
      get { return false;}
    }    

    public override void Render(float timePassed)
    {
      UpdateProgressBar();
      SetVideoProgress();			// get the percentage of playback complete so far
      Get_TimeInfo();				// show the time elapsed/total playing time
      base.Render(timePassed);		// render our controls to the screen
    }

    void FocusControl (int dwSenderId, int dwControlID, int dwParam) 
    {
      GUIMessage msg=new GUIMessage (GUIMessage.MessageType.GUI_MSG_SETFOCUS,GetID, dwSenderId, dwControlID, dwParam,0,null); 
      OnMessage(msg); 
    }


    public override void OnAction(Action action)
    {
      switch (action.wID)
      {
        case Action.ActionType.ACTION_SHOW_OSD:
        {
          return;
        }
		    

				case Action.ActionType.ACTION_NEXT_CHANNEL:
				{
					OnNextChannel();
					return;
				}
				
				case Action.ActionType.ACTION_PREV_CHANNEL:
				{
					OnPreviousChannel();
					return;
				}
			}

      base.OnAction(action);
    }

    public override bool OnMessage(GUIMessage message)
    {
      switch ( message.Message )
      {
        case GUIMessage.MessageType.GUI_MSG_WINDOW_DEINIT:	// fired when OSD is hidden
        {
          //if (g_application.m_pPlayer) g_application.m_pPlayer.ShowOSD(true);
          // following line should stay. Problems with OSD not
          // appearing are already fixed elsewhere
          FreeResources();
          return true;
        }
		    

        case GUIMessage.MessageType.GUI_MSG_WINDOW_INIT:	// fired when OSD is shown
        {
          // following line should stay. Problems with OSD not
          // appearing are already fixed elsewhere
          AllocResources();
          // if (g_application.m_pPlayer) g_application.m_pPlayer.ShowOSD(false);
          ResetAllControls();							// make sure the controls are positioned relevant to the OSD Y offset
          m_bNeedRefresh=false;
          m_dateTime=DateTime.Now;
          SetCurrentChannelLogo();
          return true;
        }
      		    

      }
      return base.OnMessage(message);
    }



    void SetVideoProgress()
    {
    
      if (g_Player.Playing)
      {
       //   double fPercentage=g_Player.CurrentPosition / g_Player.Duration;
       //       GUIProgressControl pControl = (GUIProgressControl)GetControl((int)Controls.OSD_VIDEOPROGRESS);
       //     if (null!=pControl) pControl.Percentage=(int)(100*fPercentage);			// Update our progress bar accordingly ...

        //int iValue=g_Player.Volume;
        //GUISliderControl pSlider = GetControl((int)Controls.OSD_VOLUMESLIDER) as GUISliderControl;
        //if (null!=pSlider) pSlider.Percentage=iValue;			// Update our progress bar accordingly ...
      }
    }

    void Get_TimeInfo()
    {
      
      string strChannel=GetChannelName();
      string strTime=strChannel;
      TVProgram prog=GUITVHome.Navigator.GetTVChannel(strChannel).CurrentProgram;
      if( prog!=null) 
      {
     
        strTime=String.Format("{0}-{1}", 
          prog.StartTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat),
          prog.EndTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat));
      }
			lblCurrentTime.Label=strTime;
    }

    void Handle_ControlSetting(int iControlID, long wID)
    {
    
      string strMovie=g_Player.CurrentFile;
      //      CVideoDatabase dbs;
      //    VECBOOKMARKS bookmarks;

    }

   

    public override void	 ResetAllControls()
    {
      //reset all
      bool bOffScreen=false;
      int iCalibrationY=GUIGraphicsContext.OSDOffset;
      int iTop = GUIGraphicsContext.OverScanTop;
      int iMin=0;

      foreach (CPosition pos in m_vecPositions)
      {
        pos.control.SetPosition((int)pos.XPos,(int)pos.YPos+iCalibrationY);
      }
      foreach (CPosition pos in m_vecPositions)
      {
        GUIControl pControl= pos.control;

        int dwPosY=pControl.YPosition;
        if (pControl.IsVisible)
        {
          if ( dwPosY < iTop)
          {
            int iSize=iTop-dwPosY;
            if ( iSize > iMin) iMin=iSize;
            bOffScreen=true;
          }
        }
      }
      if (bOffScreen) 
      {

        foreach (CPosition pos in m_vecPositions)
        {
          GUIControl pControl= pos.control;
          int dwPosX=pControl.XPosition;
          int dwPosY=pControl.YPosition;
          if ( dwPosY < (int)100)
          {
            dwPosY+=Math.Abs(iMin);
            pControl.SetPosition(dwPosX,dwPosY);
          }
        }
      }
      base.ResetAllControls();
    }


    public override bool NeedRefresh()
    {
      if (m_bNeedRefresh) 
      {
        m_bNeedRefresh=false;
        return true;
      }
      return false;
    }

  	private void OnPreviousChannel()
		{
			Log.Write("GUITV OSD: OnNextChannel");
  		if (!Recorder.View) return;
  		GUITVHome.Navigator.ZapToPreviousChannel(true);

  		SetCurrentChannelLogo();
  		m_dateTime = DateTime.Now;
  	}

  	private void OnNextChannel()
  	{
			
			Log.Write("GUITV ZAPOSD: OnNextChannel");
  		if (!Recorder.View) return;
  		GUITVHome.Navigator.ZapToNextChannel(true);
  		SetCurrentChannelLogo();
  		m_dateTime = DateTime.Now;
  	}

  	public void UpdateChannelInfo()
		{
			SetCurrentChannelLogo();
		}


    void SetCurrentChannelLogo()
    {
      string strChannel=GetChannelName();
      string strLogo=Utils.GetCoverArt(Thumbs.TVChannel,strChannel);
      if (System.IO.File.Exists(strLogo) )
      {
				if (imgTvChannelLogo!=null)
				{
					imgTvChannelLogo.SetFileName(strLogo);
					//img.SetPosition(GUIGraphicsContext.OverScanLeft, GUIGraphicsContext.OverScanTop);
					m_bNeedRefresh=true;
					imgTvChannelLogo.IsVisible=true;
				}
      }
      else
      {
				if (imgTvChannelLogo!=null)
				{
					imgTvChannelLogo.IsVisible=false;
				}
      }
      ShowPrograms();
    }

		string GetChannelName()
		{
			return GUITVHome.Navigator.ZapChannel;
		}
    void ShowPrograms()
    {
      if (lblOnTvNow!=null) lblOnTvNow.EnableUpDown=false;
      if (lblOnTvNext!=null) lblOnTvNext.EnableUpDown=false;


      // Set recorder status
      if (Recorder.IsRecordingChannel(GetChannelName()))
      {
				imgRecIcon.IsVisible=true;
      }
      else
			{
				imgRecIcon.IsVisible=false;
      }

      if (lblCurrentChannel!=null)
      {
				lblCurrentChannel.Label=GetChannelName();
      }

      if (lblOnTvNow!=null)
      {
				lblOnTvNow.Clear();
		
      }
      if (lblOnTvNext!=null)
      {
				lblOnTvNext.Clear();
      }

		
			TVProgram prog=GUITVHome.Navigator.GetTVChannel(GetChannelName()).GetProgramAt(m_dateTime);
      
      if (prog!=null)
      {
        string strTime=String.Format("{0}-{1}", 
          prog.StartTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat),
          prog.EndTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat));
        
        if (lblCurrentTime!=null) 
        {
					lblCurrentTime.Label=strTime;
        }
        
        strTime=String.Format("{0}", prog.StartTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat));        
        if (lblOnTvNow!=null)
        {
					lblOnTvNow.Label=prog.Title;
          GUIPropertyManager.SetProperty("#TV.View.start", strTime);
        }

        // next program
				prog=GUITVHome.Navigator.GetTVChannel(GetChannelName()).GetProgramAt(prog.EndTime.AddMinutes(1));
        if (prog!=null)
        {
          strTime=String.Format("{0} ", 
            prog.StartTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat));
        
          if (lblOnTvNext!=null)
          {
						lblOnTvNext.Label=prog.Title;
						GUIPropertyManager.SetProperty("#TV.View.stop", strTime);
          }
        }
      }
      else if (lblCurrentTime!=null)
      {
        lblCurrentTime.Label=String.Empty;
      }
      UpdateProgressBar();
    }

	  void UpdateProgressBar()
	  {
		  double fPercent;
		  if (g_Player.Playing)
		  {

				TVProgram prog=GUITVHome.Navigator.GetTVChannel(GetChannelName()).CurrentProgram;
			  if (prog==null) return;
			  string strTime=String.Format("{0}-{1}", 
				  prog.StartTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat),
				  prog.EndTime.ToString("t",CultureInfo.CurrentCulture.DateTimeFormat));

			  TimeSpan ts=prog.EndTime-prog.StartTime;
			  double iTotalSecs=ts.TotalSeconds;
			  ts=DateTime.Now-prog.StartTime; 
			  double iCurSecs=ts.TotalSeconds;
			  fPercent = ((double)iCurSecs) / ((double)iTotalSecs);
			  fPercent *=100.0d;
			  GUIPropertyManager.SetProperty("#TV.View.Percentage", ((int)fPercent).ToString());

		  }
	  }
  }
}
  