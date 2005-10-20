#region Copyright (C) 2005 Media Portal

/* 
 *	Copyright (C) 2005 Media Portal
 *	http://mediaportal.sourceforge.net
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

#endregion

using System;
using System.ComponentModel;
using System.Drawing;

namespace MediaPortal.Drawing
{
	[TypeConverter(typeof(SolidColorBrushConverter))]
	public sealed class SolidColorBrush : Brush
	{
		#region Constructors

		public SolidColorBrush()
		{
			_color = Color.Transparent;
		}

		public SolidColorBrush(Color color)
		{
			_color = color;
		}

		#endregion Constructors

		#region Methods

		public static SolidColorBrush Parse(string color)
		{
			return new SolidColorBrush(ColorTranslator.FromHtml(color));
		}

		#endregion Methods

		#region Properties

		public Color Color
		{
			get { return _color; }
			set { if(Color.Equals(_color, value) == false) { _color = value; RaiseChanged(); } }
		}

		#endregion Properties

		#region Fields

		Color						_color;

		#endregion Fields
	}
}
