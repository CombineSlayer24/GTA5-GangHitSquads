using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GangHitSquads_CS_Script
{
	public class IniFile
	{
		private Dictionary<string, Dictionary<string, string>> _iniData;

		public IniFile( string filePath )
		{
			_iniData = new Dictionary<string, Dictionary<string, string>>( StringComparer.OrdinalIgnoreCase );
			ParseIniFile( filePath );
		}

		public void ParseIniFile( string filePath )
		{
			if ( !File.Exists( filePath ) )
				throw new FileNotFoundException( "INI file not found", filePath );

			string currentSection = string.Empty;
			foreach ( var line in File.ReadAllLines( filePath ) )
			{
				var trimmedLine = line.Trim();

				// Skip comments and empty lines
				if ( trimmedLine.StartsWith( ";" ) || trimmedLine.StartsWith( "#" ) || trimmedLine.StartsWith( "//" ) || string.IsNullOrWhiteSpace( trimmedLine ) )
					continue;

				if ( trimmedLine.StartsWith( "[" ) && trimmedLine.EndsWith( "]" ) )
				{
					// New section
					currentSection = trimmedLine.Substring( 1, trimmedLine.Length - 2 );
					if ( !_iniData.ContainsKey( currentSection ) )
					{
						_iniData[ currentSection ] = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
					}
				}
				else
				{
					// Key-Value pair
					var keyValue = trimmedLine.Split( new[] { '=' }, 2 );
					if ( keyValue.Length == 2 )
					{
						var key = keyValue[0].Trim();
						var value = keyValue[1].Trim();
						if ( !string.IsNullOrEmpty( currentSection ) && !string.IsNullOrEmpty( key ) )
						{
							_iniData[ currentSection ][ key ] = value;
						}
					}
				}
			}
		}

		public string GetValue( string section, string key, string defaultValue = "" )
		{
			if ( _iniData.TryGetValue( section, out var sectionData ) )
			{
				if ( sectionData.TryGetValue( key, out var value ) )
				{
					return value;
				}
			}
			return defaultValue;
		}

		public bool GetBoolValue( string section, string key, bool defaultValue = false )
		{
			string value = GetValue( section, key, defaultValue.ToString().ToLower() );
			if ( bool.TryParse( value, out bool result ) )
			{
				return result;
			}
			return defaultValue;
		}

		public int GetIntValue( string section, string key, int defaultValue = 0 )
		{
			string value = GetValue( section, key, defaultValue.ToString() );
			if ( int.TryParse( value, out int result ) )
			{
				return result;
			}
			return defaultValue;
		}

		public float GetFloatValue( string section, string key, float defaultValue = 0.0f )
		{
			string value = GetValue( section, key, defaultValue.ToString() );
			if ( float.TryParse( value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result ) )
			{
				return result;
			}
			return defaultValue;
		}
	}

	public class EnemyScriptTest : Script
	{
		// Initalize our variables.
		public string[] sVehiclesHi, sVehiclesLo, sPedsHi, sPedsLo, sWeaponsLo, sWeaponsHi;
		public readonly List<Ped> spawnedEnemies				= new List<Ped>();
		public readonly List<Ped> enemiesToDel					= new List<Ped>();
		public Random	random									= new Random();
		public int		iMaxEnemies								= 0;
		public int		iTickRate								= 500;
		public int?     primaryColor                            = null;
		public int?     secondaryColor                          = null;
		public int      iCurrentGangIndex                        = -1;
		public bool		bShowGangBlipsOnRadar					= true;
		public bool		bisElitePed								= false;
		public float	fInVehicleGangCullDistance				= 300.0F;
		public float	fOnFootGangCullDistance					= 125.0F;

		// Dictionary Structs
		public Dictionary <Ped, Blip>		dBlip				= new Dictionary <Ped, Blip>();
		public Dictionary <int, GangData>	dGang				= new Dictionary <int, GangData>();
		public Dictionary <Ped, Vector3>	lastPositions		= new Dictionary <Ped, Vector3>();
		public Dictionary <Ped, DateTime>	lastCheckedTimes	= new Dictionary <Ped, DateTime>();
		
		// Constants
		public const float STUCK_THRESHOLD						= 0.1f; // Threshold for movement detection, in units
		public const int CHECK_INTERVAL							= 3000; // Check every 3 seconds in milliseconds
		public const int GANG_FAM								= 0;	// Families
		public const int GANG_BAL								= 1;	// Ballas
		public const int GANG_VAG								= 2;	// Vagos
		public const int GANG_GOVT								= 3;	// S.A Govt.
		public const int GANG_DUG								= 4;	// Duggans Crime Family


		// Enums
		public enum eScriptStatus								{ Off, Running }
		public eScriptStatus eState								= eScriptStatus.Off;
		public RelationshipGroup								relationshipGroupEnemies;

		// Class Structs
		public class WeaponChance
		{
			public WeaponHash WeaponHash { get; set; }
			public int Chance { get; set; }
		}

		public class GangData
		{
			public string[] VehiclesHi { get; set; }
			public string[] VehiclesLo { get; set; }
			public string[] PedsHi { get; set; }
			public string[] PedsLo { get; set; }
			public List<WeaponChance> WeaponsHi { get; set; } = new List<WeaponChance>();
			public List<WeaponChance> WeaponsLo { get; set; } = new List<WeaponChance>();
			public int? PrimaryColor { get; set; }
			public int? SecondaryColor { get; set; }
			public string NotificationMessage { get; set; }
		}

		public EnemyScriptTest()
		{
			KeyDown += OnKeyDown;
			Tick += OnTick;
			Interval = iTickRate;
			InitializeGangs();

			relationshipGroupEnemies = World.AddRelationshipGroup( "HITMEN" );

			try
			{
				IniFile config = new IniFile( "scripts\\GTA5GangHitSquads.ini" );
				bShowGangBlipsOnRadar = config.GetBoolValue( "SETTINGS", "ShowBlipsOnGangs", true );
				//iTickRate = config.GetIntValue( "SETTINGS", "ScriptTickRate", 500 );
			}
			catch ( FileNotFoundException )
			{
				ShowNotification( "Config file not found, using default settings." );
			}
			catch ( Exception ex )
			{
				ShowNotification( $"Error loading config: {ex.Message}" );
			}
		}

		/*public void ClearScriptVars()
		{
			sVehiclesHi			= null;
			sVehiclesLo			= null;
			sPedsHi				= null;
			sPedsLo				= null;
			sWeaponsLo			= null;
			sWeaponsHi			= null;
			primaryColor		= null;
			secondaryColor		= null;
			iMaxEnemies			= 0;
			iCurrentGangIndex	= -1;
			bisElitePed			= false;
			eState				= eScriptStatus.Off;
			dBlip.Clear();
			lastPositions.Clear();
			lastCheckedTimes.Clear();
			spawnedEnemies.Clear();
			enemiesToDel.Clear();
		}*/

		// Sets up our gang data
		public void InitializeGangs()
		{
			// WeaponsLo should always have the pistol at a 75% chance
			// and for Hi, a 30% chance

			// Grove Street Families
			dGang.Add( GANG_FAM, new GangData
			{
				VehiclesHi = new[] { "CAVALCADE", "BALLER", "BALLER3", "BALLER8", "PEYOTE3", "VOODOO", "PRIMO2", "TORNADO5", "MANANA2", "NEMESIS" },
				VehiclesLo = new[] { "EMPEROR", "MANANA", "TORNADO", "TORNADO5", "BUCANNEER", "PEYOTE", "PEYOTE3", "VOODOO", "PRIMO2", "BMX", "NEMESIS", "BALLER3" },
				PedsHi = new[] { "IG_VERNON", "A_M_M_OG_BOSS_01", "IG_STRETCH", "IG_LAMARDAVIS", "G_M_Y_FAMCA_01", "G_M_Y_FAMDNF_01" },
				PedsLo = new[] { "G_F_Y_FAMILIES_01", "G_M_Y_FAMCA_01", "G_M_Y_FAMDNF_01", "G_M_Y_FAMFOR_01" },
				WeaponsHi = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 60 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 50 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 30 },
				},
				WeaponsLo = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 75 },
					new WeaponChance { WeaponHash = WeaponHash.MicroSMG, Chance = 50 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.Knife, Chance = 10 }
				},
				PrimaryColor = 53,
				SecondaryColor = 53,
				NotificationMessage = "~r~Grove Street Families~w~ are out for you."
			} );
			
			// Ballas
			dGang.Add( GANG_BAL, new GangData
			{
				// Temp vehicles
				VehiclesHi = new[] { "PEYOTE3", "JACKAL", "TORNADO5", "BALLER3", "ORACLE", "BUCCANEER2", "PRIMO2", "ORACLE", "BATI", "THRUST", "SULTAN", "VOODOO", "CHINO2", "VIRGO3" },
				VehiclesLo = new[] { "BMX", "BUCCANEER2", "BUFFALO", "EMPEROR", "MANANA", "FELON", "PEYOTE", "JACKAL", "TORNADO5", "BALLER3", "ORACLE", "PRIMO2", "RUFFIAN", "BATI", "SPEEDO", "VOODOO", "SABREGT", "CHINO2", "VIRGO3" },
				PedsHi = new[] { "IG_JOHNNY_GUNS", "IG_BALLAS_LEADER", "G_M_Y_BALLAORIG_01", "G_M_Y_BALLAORIG_01" },
				PedsLo = new[] { "G_M_Y_BALLASOUT_01", "G_M_Y_BALLAEAST_01", "G_M_Y_STRPUNK_02", "G_F_Y_BALLAS_01", "G_M_Y_BALLAORIG_01" },
				WeaponsHi = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifle, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.CompactRifle, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.MachinePistol, Chance = 35 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.SawnOffShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultShotgun, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.MG, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.CombatMG, Chance = 10 },
				},
				WeaponsLo = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 75 },
					new WeaponChance { WeaponHash = WeaponHash.MicroSMG, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 20 },
					new WeaponChance { WeaponHash = WeaponHash.MachinePistol, Chance = 20 },
					new WeaponChance { WeaponHash = WeaponHash.SawnOffShotgun, Chance = 20 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 15 },
					new WeaponChance { WeaponHash = WeaponHash.CompactRifle, Chance = 15 },
					new WeaponChance { WeaponHash = WeaponHash.Knife, Chance = 10 },
				},
				PrimaryColor = 145,
				SecondaryColor = 153,
				NotificationMessage = "~r~Ballas~w~ are out balling to hunt you down!"
			} );

			// Vagos
			dGang.Add( GANG_VAG, new GangData
			{
				// Temp vehicles
				VehiclesHi = new[] { "TORNADO5", "EMPEROR", "BAGGER", "baller4", "baller3", "buccaneer2", "cavalcade", "PRIMO2", "PEYOTE3", "sabregt2", "virgo2", "landstalker2", "faction2", "contender", "voodoo", "huntley", "tampa3", "pcj", "baller7" },
				VehiclesLo = new[] { "TORNADO5", "EMPEROR", "baller3", "BMX", "buccaneer2", "cavalcade", "MANANA", "PRIMO2", "PEYOTE", "PEYOTE3", "vigero", "sabregt2", "faction2", "Phoenix", "GRANGER", "voodoo", "pcj", "baller7" },
				PedsHi = new[] { "G_M_M_MexBoss_01", "IG_Vagos_Leader", "G_M_M_MexBoss_02", "IG_VagSpeak", "MP_M_G_VagFun_01", "G_M_Y_MexGoon_02", "G_M_Y_MexGoon_02" },
				PedsLo = new[] { "G_M_Y_MexGoon_01", "G_F_Y_Vagos_01", "G_M_Y_MexGoon_02", "G_M_Y_MexGoon_03", "A_M_Y_MexThug_01" },
				WeaponsHi = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 50 },
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifle, Chance = 50 },
					new WeaponChance { WeaponHash = WeaponHash.SpecialCarbine, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.MachinePistol, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.CombatMG, Chance = 15 },
					new WeaponChance { WeaponHash = WeaponHash.MG, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.CombatMG, Chance = 10 },
				},
				WeaponsLo = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 75 },
					new WeaponChance { WeaponHash = WeaponHash.MicroSMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.MachinePistol, Chance = 20 },
					new WeaponChance { WeaponHash = WeaponHash.AssaultRifle, Chance = 15 },
					new WeaponChance { WeaponHash = WeaponHash.Knife, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.Bat, Chance = 10 },
					new WeaponChance { WeaponHash = WeaponHash.CombatMG, Chance = 5 },
				},
				PrimaryColor = 88,
				SecondaryColor = 88,
				NotificationMessage = "The ~r~Vagos~w~ tailed you and want you dead!"
			} );
			
			// San Andreas Govt.
			dGang.Add( GANG_GOVT, new GangData
			{
				// Temp vehicles
				VehiclesHi = new[] { "police", "police3", "police2", "policet", "RIOT", "police4", "police5", "SHERIFF", "sheriff2", "polterminus", "polcoquette4", "poldorado", "polgauntlet", "polimpaler5", "FBI", "FBI2" },
				VehiclesLo = new[] { "police", "police3", "police2", "policeb", "police5", "SHERIFF", "sheriff2", "polfaction2", "polterminus", "polgreenwood", "polimpaler6", "polimpaler5" },
				PedsHi = new[] { "S_M_Y_Swat_01", "S_M_Y_Cop_01", "S_M_Y_Sheriff_01", "S_M_Y_Cop_01", "S_M_Y_Sheriff_01", "S_M_Y_Marine_03", "S_M_M_CIASec_01", "S_M_M_FIBSec_01", "S_M_M_FIBOffice_01", "IG_FBISuit_01" },
				PedsLo = new[] { "S_F_Y_Cop_01", "S_M_Y_Cop_01", "S_M_Y_HwayCop_01", "S_M_Y_Sheriff_01", "S_F_Y_Sheriff_01" },
				WeaponsHi = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.SpecialCarbine, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.PistolMk2, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.SpecialCarbineMk2, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifleMk2, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.BullpupShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.SniperRifle, Chance = 20 },
				},
				WeaponsLo = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 75 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifle, Chance = 15 },
					new WeaponChance { WeaponHash = WeaponHash.Nightstick, Chance = 5 },
				},
				//PrimaryColor = 88,
				//SecondaryColor = 88,
				NotificationMessage = "The ~r~LSPD~w~ are coming for you!"
			} );

			// Duggan Crime Family (Online)
			dGang.Add( GANG_DUG, new GangData
			{
				// Temp vehicles
				VehiclesHi = new[] { "caracara2", "cavalcade3", "menacer" },
				VehiclesLo = new[] { "kamacho", "caracara", "cavalcade3" },
				PedsHi = new[] { "S_M_Y_WestSec_01", "S_M_Y_WestSec_02", "S_M_M_HighSec_05", "S_M_M_HighSec_04" },
				PedsLo = new[] { "S_M_Y_WestSec_01", "S_M_Y_WestSec_02", "S_M_M_HighSec_05", "S_M_M_HighSec_04" },
				WeaponsHi = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.SpecialCarbine, Chance = 45 },
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.MicroSMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.SpecialCarbineMk2, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifleMk2, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.BullpupRifle, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.BullpupShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.SniperRifle, Chance = 20 },
				},
				WeaponsLo = new List<WeaponChance>
				{
					new WeaponChance { WeaponHash = WeaponHash.Pistol, Chance = 75 },
					new WeaponChance { WeaponHash = WeaponHash.SMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.MicroSMG, Chance = 40 },
					new WeaponChance { WeaponHash = WeaponHash.CombatPDW, Chance = 35 },
					new WeaponChance { WeaponHash = WeaponHash.CarbineRifle, Chance = 30 },
					new WeaponChance { WeaponHash = WeaponHash.BullpupShotgun, Chance = 25 },
					new WeaponChance { WeaponHash = WeaponHash.PumpShotgun, Chance = 15 },
				},
				//PrimaryColor = 88,
				//SecondaryColor = 88,
				NotificationMessage = "The ~r~Duggans~w~ are coming for you!"
			} );

			ShowSubtitleText( $"~g~GangHitSquads Script Loaded!~w~~n~~n~~y~Press B~w~ to have random ~r~hostile~w~ encounters!", 5000 );
			Function.Call( Hash.PLAY_SOUND_FRONTEND, 0, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET" );
		}

		public void SetUpAttackingGang()
		{
			iCurrentGangIndex = GetRandomNumber( dGang.Count );
			iMaxEnemies = GetRandomNumber( 4, 12 );

			// If the player is Franklin, do not pick Families to attack.
			// If you want to exclude Grove Street Families for Franklin, for example:
			// if (GetPlayerCharacter().Model == "Player_One" && iCurrentGangIndex == GROVE_STREET_FAMILIES)
			// {
			//     iCurrentGangIndex = (iCurrentGangIndex + 1) % dGang.Count; // Select next gang if GSF
			// }

			if ( dGang.TryGetValue( iCurrentGangIndex, out var selectedGang ) )
			{
				sVehiclesHi = selectedGang.VehiclesHi;
				sVehiclesLo = selectedGang.VehiclesLo;
				sPedsHi = selectedGang.PedsHi;
				sPedsLo = selectedGang.PedsLo;
				// Handle the weapon setup inside GiveWeaponsToPed()

				primaryColor = selectedGang.PrimaryColor;
				secondaryColor = selectedGang.SecondaryColor;

				// Alert the player of which gang will attack next
				string message = $"{ selectedGang.NotificationMessage } { iMaxEnemies } Active goons!";
				ShowNotification( message, "CHAR_SIMEON" ); // PLACEHOLDER CHARACTER ICON
			}

			Function.Call( Hash.PLAY_SOUND_FRONTEND, 0, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET" );

			Wait( 1500 );
			// Player cusses the fuck out after getting this
			GetPlayerCharacter().PlayAmbientSpeech( "GENERIC_CURSE_MED" );
		}

		public void OnTick( object sender, EventArgs e )
		{
			switch ( eState )
			{
				case eScriptStatus.Running:
					ProcessScriptTick();
					CleanUpEntities( "Tick" );
				break;
			}
		}

		public void OnKeyDown( object sender, KeyEventArgs e )
		{
			// Test script if it works
			if ( e.KeyCode == Keys.B )
			{
				switch ( eState )
				{
					// Go ahead and start the script up
					case eScriptStatus.Off:
						SetUpAttackingGang();
						Function.Call( Hash.PLAY_SOUND_FRONTEND, 0, "CHARACTER_SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET" );
						eState = eScriptStatus.Running;
					break;

					// Let's end the chaos
					case eScriptStatus.Running:
						CleanUpEntities( "OnScriptEnd" );
					break;
				}
			}
		}

		// ===========================================================================
		// MONITORING AND CLEANUP FUNCTIONS
		// ===========================================================================

		public void RemoveBlipFromPed( Ped ped )
		{
			if ( dBlip.ContainsKey( ped ) )
			{
				Blip blip = dBlip[ped];
				if ( blip.Exists() )
				{
					blip.Delete();
				}
				dBlip.Remove( ped );
			}
		}		
		public void StuckMonitor()
		{
			//TODO, monitor peds that may be stuck, cull them to free up a slot in enemiesCanFight
		}

		public void CleanUpEntities( string sType )
		{
			switch ( sType )
			{
				case "Tick":
					foreach ( Ped pedCleanUp in enemiesToDel.ToList() )
					{
						if ( spawnedEnemies.Contains( pedCleanUp ) )
						{
							pedCleanUp.MarkAsNoLongerNeeded();
							spawnedEnemies.Remove( pedCleanUp );
						}
					}
					enemiesToDel.Clear();
				break;

				case "OnScriptEnd": // Cleanup for Ending the script
					foreach ( Ped member in spawnedEnemies )
					{
						// Clear peds from memory and make them flee and remove their blips
						member.MarkAsNoLongerNeeded();
						member.Weapons.RemoveAll();

						// Set these Attributes to false so they can run away after script ends
						Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, member, 46, false );  // BF_CanFightArmedPedsWhenNotArmed 
						Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, member, 5, false );   // BF_AlwaysFight 
						member.Task.FleeFrom( GetPlayerCharacter() );

						// Remove the Blip if the Ped is dead
						RemoveBlipFromPed( member );
					}
					enemiesToDel.Clear();

					Function.Call( Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player );
					//ShowNotification( "You win! They give up!", "CHAR_SIMEON" );
					ShowTutorialText( "You've survived the attack. All ~r~Enemies~w~ are retreating", 3000 );
					eState = eScriptStatus.Off;
					iMaxEnemies = 0;

					Wait( 3000 );

					int moneyAdded = 2000;
					Game.Player.Money += moneyAdded;
					ShowTutorialText( $"You've been awarded ~g~${ moneyAdded }~w~ for surviving the attack." );
					Function.Call( Hash.PLAY_SOUND_FRONTEND, 0, "PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET" );

					Wait( 2000 );
					GetPlayerCharacter().PlayAmbientSpeech( "GENERIC_THANKS" );
				break;
			}
		}

		// Anti-Stuck messure
		public bool IsPedNotMoving( Ped pEnemy )
		{
			if ( !lastPositions.ContainsKey( pEnemy ) )
			{
				lastPositions[ pEnemy ] = pEnemy.Position;
				lastCheckedTimes[ pEnemy ] = DateTime.Now;
				return false;
			}

			DateTime now = DateTime.Now;
			if ( ( now - lastCheckedTimes[ pEnemy ] ).TotalMilliseconds >= CHECK_INTERVAL )
			{
				lastCheckedTimes[ pEnemy ] = now;

				// Check if the ped is in cover
				if ( !Function.Call<bool>( Hash.IS_PED_IN_COVER, pEnemy, false ) )
				{
					Vector3 currentPosition = pEnemy.Position;
					float distanceMoved = Vector3.Distance( currentPosition, lastPositions[ pEnemy ] );

					if ( distanceMoved < STUCK_THRESHOLD )
					{
						return true;  // Ped is considered stuck
					}

					lastPositions[pEnemy] = currentPosition;
				}
			}
			return false;
		}

		public void ProcessScriptTick()
		{
			if ( eState == eScriptStatus.Running )
			{
				int enemiesCanFight = 0;
				foreach ( Ped pEnemy in spawnedEnemies )
				{
					float pedDistance = pEnemy.Position.DistanceTo( GetPlayerCharacter().Position );
					// Check if the enemy is in a vehicle
					bool isInVehicle = pEnemy.IsInVehicle();
					// Use a larger distance threshold if the enemy is in a vehicle
					float distanceThreshold = isInVehicle ? fInVehicleGangCullDistance : fOnFootGangCullDistance;
					// Make sure our ped is alive and not far away from the player.
					// If they aren't, then they are able to fight.
					if ( pEnemy.IsAlive && pedDistance < distanceThreshold )
					{
						enemiesCanFight++;
					}
					else // If not, then put them in the pool to be removed
					{
						RemoveBlipFromPed( pEnemy );
						enemiesToDel.Add( pEnemy );
					}
				}

				ShowSubtitleText( $"Enemies Can Fight: ~y~{ enemiesCanFight }~n~~w~Max Enemies: ~y~{ iMaxEnemies }" );

				if ( enemiesCanFight < iMaxEnemies )
					if ( GetRandomNumber( 500000 ) < 42500 )
						if ( GetRandomNumber( 100 ) > 25 )
							SpawnFootEnemy();
						else
							SpawnVehicleEnemy();
			}
		}

		// ===========================================================================
		// PED RELATED FUNCTIONS
		// ===========================================================================

		public void GiveWeaponsToPed( Ped pEnemy )
		{
			if ( iCurrentGangIndex != -1 && dGang.TryGetValue( iCurrentGangIndex, out GangData currentGang ) )
			{
				List<WeaponChance> chosenWeapons = bisElitePed ? currentGang.WeaponsHi : currentGang.WeaponsLo;
				WeaponChance selectedWeapon = GetRandomWeaponFromList( chosenWeapons );
				pEnemy.Weapons.Give( selectedWeapon.WeaponHash, 9999, true, true );
			}
			else
			{
				ShowNotification( "Error: No gang data for weapon distribution.", "CHAR_SIMEON", "Warning", "~r~Error" );
			}
		}

		// Helper method to select a weapon based on probability
		public WeaponChance GetRandomWeaponFromList( List<WeaponChance> weaponChances )
		{
			int totalChance = weaponChances.Sum( w => w.Chance );
			int randomValue = GetRandomNumber( totalChance );

			foreach ( var weapon in weaponChances )
			{
				randomValue -= weapon.Chance;
				if ( randomValue < 0 )
				{
					return weapon;
				}
			}

			// Fallback, should not happen if chances are properly set
			return weaponChances.First();
		}

		public void CreatePedInVehicle( Vehicle enemyVehicle, string pedModel, VehicleSeat seat )
		{
			try
			{
				Ped pEnemy = World.CreatePed( pedModel, enemyVehicle.Position );
				if ( IsValidEntity( pEnemy ) )
				{
					pEnemy.SetIntoVehicle( enemyVehicle, seat ); // Set initial position
					pEnemy.Task.WarpIntoVehicle( enemyVehicle, seat ); // Warp to make sure they are in the vehicle
					SetPedInfo( pEnemy );

					if ( seat == VehicleSeat.Driver )
					{
						pEnemy.Task.DriveTo( enemyVehicle, GetPlayerCharacter().Position, 90f, VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless, 2.0f );
					}
				}
			}
			catch ( Exception )
			{
				throw new Exception( $"Invalid ped model: { pedModel }" );
			}
		}

		public void SpawnVehicleEnemy()
		{
			string enemyVehicleModel;
			string enemyPedModel;
			Vector3 spawnPos = SetSpawnAroundPlayer( "Vehicle" );

			if ( GetRandomNumber( 100 ) < 25 )
			{
				enemyVehicleModel = RandomChoice( sVehiclesHi );
				enemyPedModel = RandomChoice( sPedsHi );
				bisElitePed = true;
			}
			else
			{
				enemyVehicleModel = RandomChoice( sVehiclesLo );
				enemyPedModel = RandomChoice( sPedsLo );
				bisElitePed = false;
			}

			try
			{
				Vehicle enemyVehicle = World.CreateVehicle(enemyVehicleModel, spawnPos);
				if ( IsValidEntity( enemyVehicle ) )
				{
					enemyVehicle.PlaceOnNextStreet();

					// Get the road position and heading
					OutputArgument roadPos = new OutputArgument();
					OutputArgument roadHeading = new OutputArgument();
					Function.Call( Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, spawnPos.X, spawnPos.Y, spawnPos.Z, roadPos, roadHeading );

					Vector3 realRoadPos = roadPos.GetResult<Vector3>();
					float realRoadHeading = roadHeading.GetResult<float>();

					// Place the vehicle on the road with the correct heading for traffic
					enemyVehicle.Position = realRoadPos;
					enemyVehicle.Heading = realRoadHeading;

					// Use native function to set vehicle colors
					if ( primaryColor.HasValue || secondaryColor.HasValue )
					{
						int pColor = primaryColor.HasValue ? primaryColor.Value : -1; // -1 means don't change
						int sColor = secondaryColor.HasValue ? secondaryColor.Value : -1;
						Function.Call( Hash.SET_VEHICLE_COLOURS, enemyVehicle, pColor, sColor );
					}

					RandomizeVehicleMods( enemyVehicle );

					// Create the driver
					CreatePedInVehicle( enemyVehicle, enemyPedModel, VehicleSeat.Driver );

					// Determine how many seats are available
					int availableSeats = CalculateAvailableSeats( enemyVehicle ) - 1; // Subtract 1 for the driver

					// Array of probabilities for additional passengers, but only if there are seats available
					int[] additionalPassengers = { 80, 50, 32, 18 };
					int passengerCount = 0;
					for ( int seatIndex = 0; seatIndex < 16 && passengerCount < availableSeats; seatIndex++ ) // Loop through possible seats
					{
						if ( GetRandomNumber( 100 ) < additionalPassengers[ passengerCount % additionalPassengers.Length ] ) // Cycle through probabilities
						{
							VehicleSeat seat = ( VehicleSeat )seatIndex;
							if ( enemyVehicle.IsSeatFree( seat ) )
							{
								string passengerModel = bisElitePed ? RandomChoice( sPedsHi ) : RandomChoice( sPedsLo );
								CreatePedInVehicle( enemyVehicle, passengerModel, seat );
								passengerCount++;
							}
							else
								continue;	// If the seat isn't free, we continue to the next seat without increasing passengerCount
						}
					}

					// Add Blip
					Blip blip = enemyVehicle.AddBlip();
					if ( blip != null )
						ConfigureBlip( blip, "Vehicle" );

					// We should probably do this the same with on foot peds
					// Mark them no longer needed if they get too far away from ply.
					enemyVehicle.MarkAsNoLongerNeeded();
				}
			}
			catch ( Exception )
			{
				ShowNotification( $"Invalid vehicle model: {enemyVehicleModel}" );
			}
		}

		public int CalculateAvailableSeats( Vehicle vehicle )
		{
			int count = 0;
			for ( int seatIndex = -1; seatIndex <= 15; seatIndex++ ) // -1 for driver, 0-15 for passengers
			{
				VehicleSeat seat = (VehicleSeat)seatIndex;
				if ( vehicle.IsSeatFree( seat ) )
				{
					count++;
				}
			}
			return count;
		}

		public Vector3 SetSpawnAroundPlayer( string sSpawnType = "StreetPed" )
		{
			Vector3 playerPos = GetPlayerCharacter().Position;
			Vector3 randomPos;
			float minDistance = 25, maxDistance = 60;

			if ( GetPlayerCharacter().IsInVehicle() )
			{
				if ( sSpawnType == "Vehicle" )
				{
					minDistance = 175;
					maxDistance = 200;
				}
				else if ( sSpawnType == "StreetPed" )
				{
					minDistance = 25;
					maxDistance = 60;
				}
			}
			else
			{
				if ( sSpawnType == "Vehicle" )
				{
					minDistance = 100;
					maxDistance = 150;
				}
				else if ( sSpawnType == "StreetPed" )
				{
					minDistance = 25;
					maxDistance = 60;
				}
			}

			randomPos = playerPos.Around( GetRandomNumber( ( int ) minDistance, ( int ) maxDistance ) );

			switch ( sSpawnType )
			{
				case "StreetPed":
					return World.GetNextPositionOnSidewalk( randomPos );
				case "Vehicle":
					return World.GetNextPositionOnStreet( randomPos );
				default:
					return randomPos; // Return the random position if an unknown type is provided
			}
		}

		public void SpawnFootEnemy()
		{
			string enemyPedModel;
			//enemyPedModel = RandomChoice( sPedsLo );
			if ( GetRandomNumber( 100 ) < 25 )
			{
				enemyPedModel = RandomChoice( sPedsHi );
				bisElitePed = true;
			}
			else
			{
				enemyPedModel = RandomChoice( sPedsLo );
				bisElitePed = false;
			}

			Vector3 spawnPos = SetSpawnAroundPlayer( "StreetPed" );
			Ped pEnemy = World.CreatePed( enemyPedModel, spawnPos );

			SetPedInfo( pEnemy );
		}

		public void SetPedInfo( Ped pEnemy )
		{
			if ( IsValidEntity( pEnemy ) )
			{
				//TODO: Do difficulty Settings based on stats
				if ( bisElitePed )
				{
					pEnemy.MaxHealth = 400;
					pEnemy.Health = 400;
				}
				else
				{
					pEnemy.MaxHealth = 200;
					pEnemy.Health = 200;
				}

				pEnemy.Accuracy = 50;
				pEnemy.ShootRate = 100;
				pEnemy.CanSwitchWeapons = true;

				// Have a 25% chance for Elite Peds to get some armor, if not elite, no armor added
				pEnemy.Armor = bisElitePed && GetRandomNumber( 100 ) > 75 ? GetRandomNumber( 25, 50 ) : 0;
				pEnemy.Money = GetRandomNumber( 40 );

				GiveWeaponsToPed( pEnemy );
				RandomizePedAppearance( pEnemy );

				Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, pEnemy, 0, true );   // CanUseCover
				Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, pEnemy, 46, true );  // BF_CanFightArmedPedsWhenNotArmed 
				Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, pEnemy, 5, true );   // BF_AlwaysFight 
				Function.Call( Hash.SET_PED_COMBAT_ATTRIBUTES, pEnemy, 68, false ); // BF_DisableReactToBuddyShot

				Function.Call( Hash.SET_PED_CONFIG_FLAG, pEnemy, 107, false );      // CPED_CONFIG_FLAG_DontActivateRagdollFromBulletImpact
				Function.Call( Hash.SET_PED_CONFIG_FLAG, pEnemy, 227, true );       // CPED_CONFIG_FLAG_ForceRagdollUponDeath

				// Set relationships
				// Make sure all hitmen are in the same relationship group
				if ( pEnemy.RelationshipGroup != relationshipGroupEnemies )
				{
					pEnemy.RelationshipGroup = relationshipGroupEnemies;
				}

				// Set relationship with player to hate
				Function.Call( Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, relationshipGroupEnemies, Game.Player.Character.RelationshipGroup ); // 5 is for Hate
				Function.Call( Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, Game.Player.Character.RelationshipGroup, relationshipGroupEnemies );

				Function.Call( Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 1, relationshipGroupEnemies, relationshipGroupEnemies ); // Make sure hitmen like each other within the group


				pEnemy.Task.ClearAll();
				pEnemy.Task.Combat( GetPlayerCharacter() );

				// Add Blip
				Blip blip = pEnemy.AddBlip();
				ConfigureBlip( blip, "StreetPed" );
				dBlip.Add( pEnemy, blip );

				spawnedEnemies.Add( pEnemy );
			}
		}

		public void ConfigureBlip( Blip blip, string sBlipType = "StreetPed" )
		{
			if ( bShowGangBlipsOnRadar && blip != null )
			{
				if ( sBlipType == "StreetPed" )
				{
					blip.Sprite = BlipSprite.Standard;
					blip.Scale = 0.65f;
					if ( bisElitePed )
					{
						//blip.Sprite = BlipSprite.BountyHit;	// I perfer the level up or down icon on the standard
						blip.Color = BlipColor.Yellow;
						blip.Name = "Elite Hitman";
					}
					else
					{
						blip.Color = BlipColor.Red;
						blip.Name = "Hitman";
					}
				}
				if ( sBlipType == "Vehicle" )
				{
					blip.Sprite = BlipSprite.Standard;
					blip.Scale = 1.0f;
					if ( bisElitePed )
					{
						//blip.Sprite = BlipSprite.BountyHit;
						blip.Color = BlipColor.Yellow;
						blip.Name = "Elite Hitman Vehicle";
					}
					else
					{
						blip.Color = BlipColor.Red;
						blip.Name = "Hitman Vehicle";
					}
				}
				if ( sBlipType == "Helicopter" )
				{
					// Do this later.
					return;
				}
			}
		}

		public async Task FadeInBlip( Blip blip, int durationInMs = 750 )
		{
			if ( blip != null && blip.Exists() )
			{
				int steps = 10; // Number of steps for fading in
				int delay = durationInMs / steps; // Time between each step

				for ( int i = 0; i <= steps; i++ )
				{
					float alpha = ( float ) i / steps; // Calculate alpha based on current step
					Function.Call( Hash.SET_BLIP_ALPHA, blip, ( int ) ( alpha * 255 ) ); // Set the alpha of the blip
					await Task.Delay( delay ); // Wait for the next step
				}
				Function.Call( Hash.SET_BLIP_ALPHA, blip, 255 ); // Ensure it's fully visible at the end
			}
		}

		public void RandomizeVehicleMods( Vehicle vehicle )
		{
			for ( int modType = 0; modType < 50; modType++ ) // There are roughly 50 ish mod types in GTA V, though not all are used for every vehicle
			{
				int modCount = Function.Call<int>( Hash.GET_NUM_VEHICLE_MODS, vehicle, modType );
				if ( modCount > 0 )
				{
					// -1 is for stock, so we start from 0 for customizing
					int randomMod = GetRandomNumber( 0, modCount );
					Function.Call( Hash.SET_VEHICLE_MOD, vehicle, modType, randomMod, false ); // false here means do not use custom tire smoke
				}
			}

			// Randomize wheels specifically since they have a separate native function
			int wheelType = GetRandomNumber( 0, 7 ); // 0-6 are valid wheel types
			Function.Call( Hash.SET_VEHICLE_WHEEL_TYPE, vehicle, wheelType );
			int wheelModCount = Function.Call<int>( Hash.GET_NUM_VEHICLE_MODS, vehicle, 23 ); // 23 is the mod type for wheels
			if ( wheelModCount > 0 )
			{
				int randomWheel = GetRandomNumber( 0, wheelModCount);
				Function.Call( Hash.SET_VEHICLE_MOD, vehicle, 23, randomWheel, false );
			}
		}

		public void RandomizePedAppearance( Ped ped )
		{
			// Randomize clothes
			for ( int i = 0; i < 12; i++ ) // 12 is the guess number of clothing components in GTA V
			{
				int drawableId = Function.Call<int>( Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, ped, i );
				if ( drawableId > 0 )
				{
					int randomDrawable = GetRandomNumber( drawableId );
					Function.Call( Hash.SET_PED_COMPONENT_VARIATION, ped, i, randomDrawable, 0, 0 );
				}
			}

			// Randomize props like hats and glasses
			for ( int i = 0; i < 3; i++ )
			{
				int propId = Function.Call<int>( Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, ped, i );
				if ( propId > 0 )
				{
					if ( GetRandomNumber( 100 ) > 50 ) // 50% chance to wear the prop
					{
						int randomProp = GetRandomNumber( propId );
						int textureCount = Function.Call<int>( Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS, ped, i, randomProp );
						int randomTexture = GetRandomNumber(textureCount);
						Function.Call( Hash.SET_PED_PROP_INDEX, ped, i, randomProp, randomTexture, true );
					}
					else
					{
						Function.Call( Hash.CLEAR_PED_PROP, ped, i );
					}
				}
			}
		}

		public static void ShowSubtitleText( string sText, int iDuration = 2500 )
		{
			Function.Call( Hash.BEGIN_TEXT_COMMAND_PRINT, "CELL_EMAIL_BCON" );
			Function.Call( Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, sText );
			Function.Call( Hash.END_TEXT_COMMAND_PRINT, iDuration, true );
		}
		public static Ped GetPlayerCharacter()
		{
			return Game.Player.Character;
		}

		public static void ShowNotification( string sText, string sIconImage = "CHAR_SIMEON", string sSubjectText = "Marked", string sSenderText = "~c~Simeon" )
		{
			Function.Call( Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING" );
			Function.Call( Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, sText );
			Function.Call( Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT, sIconImage, sIconImage, true, 1, sSubjectText, sSenderText );
		}

		public static void ShowTutorialText( string sMessage, int iDuration = 5000, bool bSound = true )
		{
			Function.Call( Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING" );
			Function.Call( Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, sMessage );
			Function.Call( Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, bSound, iDuration );
		}


		// ===========================================================================
		// HELPER FUNCTIONS
		// ===========================================================================
		public int GetRandomNumber( int iMax )
		{
			return random.Next( iMax );
		}

		public int GetRandomNumber( int iMin, int iMax )
		{
			return random.Next( iMin, iMax );
		}

		static public bool IsValidEntity( Entity entity )
		{
			return entity != null && entity.Exists();
		}

		// Pick a random array option
		public T RandomChoice<T>( T[] array )
		{
			return array[ GetRandomNumber( 0, array.Length )];
		}

		// Insert / Merge an array tables into 1.
		// Used for combining all gangs into 1.
		public string[] MergeArrays( params string[][] arrays )
		{
			int totalLength = 0;
			foreach ( string[] array in arrays )
			{
				totalLength += array.Length;
			}
			string[] result = new string[ totalLength ];

			int currentIndex = 0;
			foreach ( string[] array in arrays )
			{
				Array.Copy( array, 0, result, currentIndex, array.Length );
				currentIndex += array.Length;
			}

			return result;
		}
	}
}