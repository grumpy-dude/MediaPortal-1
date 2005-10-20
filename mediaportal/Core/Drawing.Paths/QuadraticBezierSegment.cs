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

namespace MediaPortal.Drawing.Paths
{
	public class QuadraticBezierSegment : PathSegment
	{
		#region Constructors

		public QuadraticBezierSegment()
		{
			_point1 = Point.Empty;
			_point2 = Point.Empty;
		}

		public QuadraticBezierSegment(Point controlPoint, Point endPoint, bool isStroked) : base(isStroked)
		{
			_point1 = controlPoint;
			_point2 = endPoint;
		}

		#endregion Constructors

		#region Methods

		public void Blah()
		{
//			cairo_get_current_point (svg_cairo->cr, &x, &y);
//			cairo_curve_to(new Point(x  + 2.0/3.0 * (x1 - x),  y  + 2.0/3.0 * (y1 - y)), new Point(x2 + 2.0/3.0 * (x1 - x2), y2 + 2.0/3.0 * (y1 - y2)), new Point(x2, y2));
		}

		#endregion Methods

		#region Properties
	
		public Point Point1
		{
			get { return _point1; }
			set { if(Point.Equals(_point1, value) == false) { _point1 = value; RaiseChanged(); } }
		}
		
		public Point Point2
		{
			get { return _point2; }
			set { if(Point.Equals(_point2, value) == false) { _point2 = value; RaiseChanged(); } }
		}

		#endregion Properties

		#region Fields

		Point						_point1;
		Point						_point2;

		#endregion Fields
	}
}
