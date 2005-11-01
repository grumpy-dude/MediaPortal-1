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

namespace System.ComponentModel
{
#if !NET_2_0

    public class DoWorkEventArgs : CancelEventArgs
	{
		#region Constructors

		public DoWorkEventArgs(object argument)
		{
			_argument = argument;
		}

		#endregion Constructors

		#region Properties

		public object Argument
		{
			get { return _argument; }
		}

		public object Result
		{
			get { return _result; }
			set { _result = value; }
		}

		#endregion Properties

		#region Fields

		readonly object				_argument;
		object						_result;

		#endregion Fields
	}

#endif
}
