﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Blueprints
{
    public class Designator_Blueprint : Designator
    {
        private readonly float   _lineOffset = -0f;
        private          float   _middleMouseDownTime;
        private          float   _panelHeight    = 999f;
        private          Vector2 _scrollPosition = Vector2.zero;

        /* Without defaultDesc (does defaultLable also count?) game throws this error as of June 07, 2023:
         *    Exception filling window for Verse.ImmediateWindow: System.NullReferenceException: Object reference not set to an instance of an object
              at Verse.GenText.TruncateHeight (System.String str, System.Single width, System.Single height, System.Collections.Generic.Dictionary`2[TKey,TValue] cache) [0x00029] in <95de19971c5d40878d8742747904cdcd>:0 
              at RimWorld.ArchitectCategoryTab+<>c__DisplayClass22_0.<DoInfoBox>b__0 () [0x000df] in <95de19971c5d40878d8742747904cdcd>:0 
              at Verse.ImmediateWindow.DoWindowContents (UnityEngine.Rect inRect) [0x00000] in <95de19971c5d40878d8742747904cdcd>:0 
              at Verse.Window.InnerWindowOnGUI (System.Int32 x) [0x001d3] in <95de19971c5d40878d8742747904cdcd>:0  
            (Filename: C:\buildslave\unity\build\Runtime/Export/Debug/Debug.bindings.h Line: 39)
        */
        public Designator_Blueprint( Blueprint blueprint )
        {
            Blueprint      = blueprint;
            icon           = Resources.Icon_Blueprint;
            soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
            defaultDesc = "Default";
            defaultLabel = "Default";
        }

        public Blueprint Blueprint { get; }

        public override int    DraggableDimensions => 0;
        public override string Label               => Blueprint.name;

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                var options = new List<FloatMenuOption>();

                // rename
                options.Add( new FloatMenuOption( "Fluffy.Blueprints.Rename".Translate(),
                                                  delegate
                                                  {
                                                      Find.WindowStack.Add( new Dialog_NameBlueprint( Blueprint ) );
                                                  } ) );

                // delete blueprint
                options.Add( new FloatMenuOption( "Fluffy.Blueprints.Remove".Translate(),
                                                  delegate { BlueprintController.Remove( this, false ); } ) );

                // delete blueprint and remove from disk
                if ( Blueprint.exported )
                    options.Add( new FloatMenuOption( "Fluffy.Blueprints.RemoveAndDeleteXML".Translate(),
                                                      delegate { BlueprintController.Remove( this, true ); } ) );

                // store to xml
                else
                    options.Add( new FloatMenuOption( "Fluffy.Blueprints.SaveToXML".Translate(),
                                                      delegate { BlueprintController.SaveToXML( Blueprint ); } ) );

                return options;
            }
        }

        public override AcceptanceReport CanDesignateCell( IntVec3 loc )
        {
            // always return true - we're looking at the larger blueprint in SelectedUpdate() and DesignateSingleCell() instead.
            return true;
        }

        // note that even though we're designating a blueprint, as far as the game is concerned we're only designating the _origin_ cell
        public override void DesignateSingleCell( IntVec3 origin )
        {
            var somethingSucceeded = false;
            var planningMode       = Event.current.shift;

            // looping through cells, place where needed.
            foreach ( var item in Blueprint.AvailableContents )
            {
                var placementReport = item.CanPlace( origin );
                if ( planningMode && placementReport != PlacementReport.AlreadyPlaced )
                {
                    item.Plan( origin );
                    somethingSucceeded = true;
                }

                if ( !planningMode && placementReport == PlacementReport.CanPlace )
                {
                    item.Designate( origin );
                    somethingSucceeded = true;
                }
            }

            // TODO: Add succeed/failure sounds, failure reasons
            Finalize( somethingSucceeded );
        }

        // copy-pasta from RimWorld.Designator_Place, with minor changes.
        public override void DoExtraGuiControls( float leftX, float bottomY )
        {
            var height = 90f;
            var width  = 200f;

            var margin     = 9f;
            var topmargin  = 15f;
            var numButtons = 3;
            var button     = Mathf.Min( ( width - ( numButtons + 1 ) * margin ) / numButtons, height - topmargin );

            var winRect = new Rect( leftX, bottomY - height, width, height );
            HandleRotationShortcuts();

            Find.WindowStack.ImmediateWindow( 73095, winRect, WindowLayer.GameUI, delegate
            {
                var rotationDirection = RotationDirection.None;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font   = GameFont.Medium;

                var rotLeftRect = new Rect( margin, topmargin, button, button );
                Widgets.Label( rotLeftRect, KeyBindingDefOf.Designator_RotateLeft.MainKeyLabel );
                if ( Widgets.ButtonImage( rotLeftRect, Resources.RotLeftTex ) )
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    rotationDirection = RotationDirection.Counterclockwise;
                    Event.current.Use();
                }

                var flipRect = new Rect( 2 * margin + button, topmargin, button, button );
                Widgets.Label( flipRect, KeyBindingDefOf2.Blueprint_Flip.MainKeyLabel );
                if ( Widgets.ButtonImage( flipRect, Resources.FlipTex ) )
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    Blueprint.Flip();
                    Event.current.Use();
                }

                var rotRightRect = new Rect( 3 * margin + 2 * button, topmargin, button, button );
                Widgets.Label( rotRightRect, KeyBindingDefOf.Designator_RotateRight.MainKeyLabel );
                if ( Widgets.ButtonImage( rotRightRect, Resources.RotRightTex ) )
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    rotationDirection = RotationDirection.Clockwise;
                    Event.current.Use();
                }


                if ( rotationDirection != RotationDirection.None )
                    Blueprint.Rotate( rotationDirection );
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font   = GameFont.Small;
            } );
        }

        public override void DrawPanelReadout( ref float curY, float width )
        {
            // we need the window's size to be able to do a scrollbar, but are not directly given that size.
            var outRect = ArchitectCategoryTab.InfoRect.AtZero();

            // our window actually starts from curY
            outRect.yMin = curY;

            // our contents height is given by final curY - which we conveniently saved
            var viewRect = new Rect( 0f, 0f, width, _panelHeight );

            // if contents are larger than available canvas, leave some room for scrollbar
            if ( viewRect.height > outRect.height )
            {
                outRect.width -= 16f;
                width         -= 16f;
            }

            // since we're going to work in new GUI group (through the scrollview), we'll need to keep track of
            // our own relative Y position.
            var oldY = curY;
            curY = 0f;

            // start scrollrect
            Widgets.BeginScrollView( outRect, ref _scrollPosition, viewRect );

            // list of objects to be build
            Text.Font = GameFont.Tiny;
            foreach ( var buildables in Blueprint.GroupedBuildables )
            {
                // count
                var curX = 5f;
                Widgets.Label( new Rect( 5f, curY, width * .2f, 100f ), buildables.Value.Count + "x" );
                curX += width * .2f;

                // stuff selector
                var height = 0f;
                if ( buildables.Value.First().Stuff != null )
                {
                    var label = buildables.Value.First().Stuff.LabelAsStuff.CapitalizeFirst() + " " +
                                buildables.Key.label;

                    var iconRect = new Rect( curX, curY, 12f, 12f );
                    curX += 16f;

                    height = Text.CalcHeight( label, width - curX ) + _lineOffset;
                    var labelRect = new Rect( curX, curY, width - curX, height );
                    var buttonRect = new Rect( curX   - 16f, curY, width - curX + 16f,
                                               height + _lineOffset );

                    if ( Mouse.IsOver( buttonRect ) )
                    {
                        GUI.DrawTexture( buttonRect, TexUI.HighlightTex );
                        GUI.color = GenUI.MouseoverColor;
                    }

                    GUI.DrawTexture( iconRect, Resources.Icon_Edit );
                    GUI.color = Color.white;
                    Widgets.Label( labelRect, label );
                    if ( Widgets.ButtonInvisible( buttonRect ) )
                        Blueprint.DrawStuffMenu( buildables.Key );
                }
                else
                {
                    // label
                    var labelWidth = width - curX;
                    var label      = buildables.Key.LabelCap;
                    height = Text.CalcHeight( label, labelWidth ) + _lineOffset;
                    Widgets.Label( new Rect( curX, curY, labelWidth, height ), label );
                }

                // next line
                curY += height + _lineOffset;
            }

            // complete cost list
            curY      += 12f;
            Text.Font =  GameFont.Small;
            Widgets.Label( new Rect( 0f, curY, width, 24f ), "Fluffy.Blueprint.Cost".Translate() );
            curY += 24f;

            Text.Font = GameFont.Tiny;
            var costlist = Blueprint.CostListAdjusted;
            for ( var i = 0; i < costlist.Count; i++ )
            {
                var       thingCount = costlist[i];
                Texture2D image;
                if ( thingCount.ThingDef == null )
                    image = BaseContent.BadTex;
                else
                    image = thingCount.ThingDef.uiIcon;
                GUI.DrawTexture( new Rect( 0f, curY, 20f, 20f ), image );
                if ( thingCount.ThingDef !=
                     null &&
                     thingCount.ThingDef.resourceReadoutPriority !=
                     ResourceCountPriority.Uncounted &&
                     Find.CurrentMap.resourceCounter.GetCount( thingCount.ThingDef ) < thingCount.Count )
                    GUI.color = Color.red;
                Widgets.Label( new Rect( 26f, curY + 2f, 50f, 100f ), thingCount.Count.ToString() );
                GUI.color = Color.white;
                string text;
                if ( thingCount.ThingDef == null )
                    text = "(" + "UnchosenStuff".Translate() + ")";
                else
                    text = thingCount.ThingDef.LabelCap;
                var height = Text.CalcHeight( text, width - 60f ) - 2f;
                Widgets.Label( new Rect( 60f, curY + 2f, width - 60f, height ), text );
                curY += height + _lineOffset;
            }

            Widgets.EndScrollView();

            _panelHeight = curY;

            // need to give some extra offset to properly align description.
            // also, add back in our internal offset
            curY += 28f + oldY;
        }

        public override bool GroupsWith( Gizmo other )
        {
            return Blueprint == ( other as Designator_Blueprint ).Blueprint;
        }

        public override void Selected()
        {
            base.Selected();
            Blueprint.RecacheBuildables();
            if ( !Blueprint.AvailableContents.Any() )
            {
                Messages.Message( "Fluffy.Blueprints.NothingAvailableInBlueprint".Translate( Blueprint.name ),
                                  MessageTypeDefOf.RejectInput );
            }
            else
            {
                var unavailable = Blueprint.contents.Except( Blueprint.AvailableContents )
                                           .Select( bi => bi.BuildableDef.label ).Distinct();
                if ( unavailable.Any() )
                    Messages.Message(
                        "Fluffy.Blueprints.XNotAvailableInBlueprint".Translate(
                            Blueprint.name, string.Join( ", ", unavailable.ToArray() ) ),
                        MessageTypeDefOf.CautionInput );
            }
        }

        public override void SelectedUpdate()
        {
            GenDraw.DrawNoBuildEdgeLines();
            if ( !ArchitectCategoryTab.InfoRect.Contains( UI.MousePositionOnUI ) )
            {
                var origin = UI.MouseCell();
                Blueprint.DrawGhost( origin );
            }
        }

        // Copy-pasta from RimWorld.HandleRotationShortcuts()
        private void HandleRotationShortcuts()
        {
            var rotationDirection = RotationDirection.None;
            if ( Event.current.button == 2 )
            {
                if ( Event.current.type == EventType.MouseDown )
                {
                    Event.current.Use();
                    _middleMouseDownTime = Time.realtimeSinceStartup;
                }

                if ( Event.current.type == EventType.MouseUp && Time.realtimeSinceStartup - _middleMouseDownTime < 0.15f )
                    rotationDirection = RotationDirection.Clockwise;
            }


            if ( KeyBindingDefOf.Designator_RotateRight.KeyDownEvent )
                rotationDirection = RotationDirection.Clockwise;
            if ( KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent )
                rotationDirection = RotationDirection.Counterclockwise;
            if ( KeyBindingDefOf2.Blueprint_Flip.KeyDownEvent )
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                Blueprint.Flip();
            }

            if ( rotationDirection == RotationDirection.Clockwise )
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                Blueprint.Rotate( RotationDirection.Clockwise );
            }

            if ( rotationDirection == RotationDirection.Counterclockwise )
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                Blueprint.Rotate( RotationDirection.Counterclockwise );
            }
        }
    }
}