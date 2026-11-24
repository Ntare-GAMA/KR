// Prototype Collection - Street Props
// Copyright (c) Amplify Creations, Lda <info@amplify.pt>

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AmplifyCreations.PrototypeCollection.StreetProps
{
	public class Preferences
	{		
		private static readonly GUIContent StartUp = new GUIContent( "Show start screen on Unity launch" );
		public static readonly string PrefHashGUIDBase64 = "Kz/ZJqsj7kSIf9yCXV4UdA==";
		// Kigali Rush: read lazily. As a static field it ran inside the StartScreen ScriptableObject's constructor,
		// where Unity forbids Application.productName (a red UnityException in the Console on every load).
		public static string PrefStartUp => PrefHashGUIDBase64 + Application.productName;
		public static bool GlobalStartUp = true;
	}
}