using System;
using System.Linq;
using System.Reflection;

namespace BDArmory.ModIntegration
{
	public static class PhysicsRangeExtender
	{
		static bool havePRE = false;
		static PropertyInfo modEnabled = null; // bool property
		static PropertyInfo PRERange = null; // int property
		public static bool CheckForPhysicsRangeExtender()
		{
			using var a = AppDomain.CurrentDomain.GetAssemblies().ToList().GetEnumerator();
			while (a.MoveNext())
			{
				if (a.Current.FullName.Split([','])[0] == "PhysicsRangeExtender")
				{
					havePRE = true;
					foreach (var t in a.Current.GetTypes())
					{
						if (t != null && t.Name == "PreSettings")
						{
							var PREInstance = UnityEngine.Object.FindObjectOfType(t);
							foreach (var propInfo in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
							{
								if (propInfo == null) continue;
								switch (propInfo.Name)
								{
									case "ModEnabled":
										modEnabled = propInfo;
										break;
									case "GlobalRange":
										PRERange = propInfo;
										break;
								}
							}
						}
					}
					break;
				}
			}
			return havePRE;
		}
		public static bool IsPREEnabled => modEnabled != null && (bool)modEnabled.GetValue(null);
		public static float GetPRERange()
		{
			if (PRERange == null) return 0;
			return (int)PRERange.GetValue(null) * 1000f;
		}
	}
}