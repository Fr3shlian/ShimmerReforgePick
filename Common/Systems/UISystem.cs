using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace ShimmerReforgePick.Common.Systems {
    class UISystem : ModSystem {
        private UserInterface ui;
        private ReforgePickUI reforgePickUI;
        private GameTime lastUpdateUiGameTime;

        public override void Load() {
            if (!Main.dedServ) {
                IL_Main.DrawInventory += DrawReforgeUI;
                IL_Main.CraftItem += PickReforge;
                On_ItemSlot.MouseHover_ItemArray_int_int += ShowTheorheticalStats;

                ui = new UserInterface();
                reforgePickUI = new ReforgePickUI();
                reforgePickUI.Activate();
                ui.SetState(reforgePickUI);
            }
        }

        private void ShowTheorheticalStats(On_ItemSlot.orig_MouseHover_ItemArray_int_int orig, Item[] inv, int context, int slot) {
            Item item = inv[slot];

            if (context == ItemSlot.Context.CraftingMaterial && reforgePickUI.reforgeList.selectedRecipe?.type == item.type && reforgePickUI.reforgeList.desiredPrefix != -1) {
                Item noPrefix = item.Clone();

                item.Prefix(reforgePickUI.reforgeList.desiredPrefix);

                orig(inv, context, slot);
                inv[slot] = noPrefix;
            } else {
                orig(inv, context, slot);
            }
        }

        private void PickReforge(ILContext il) {
            try {
                ILCursor c = new(il);

                if (c.TryGotoNext(
                    i => i.OpCode == OpCodes.Ldc_I4_M1,
                    i => i.MatchCallvirt(typeof(Item), "Prefix")
                )) {
                    c.Index++;

                    c.EmitDelegate<Func<int, int>>((_) => {
                        if (reforgePickUI.reforgeList.desiredPrefix != -1 && reforgePickUI.reforgeList.selectedRecipe != null) {
                            return reforgePickUI.reforgeList.desiredPrefix;
                        }

                        return -1;
                    });
                } else {
                    throw new Exception();
                }
            } catch {
                MonoModHooks.DumpIL(ModContent.GetInstance<ShimmerReforgePick>(), il);
            }
        }

        private void DrawReforgeUI(ILContext il) {
            try {
                ILCursor c = new(il);

                if (c.TryGotoNext(
                    i => i.MatchCallvirt(typeof(SpriteBatch), "Draw"),
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v && v.Index == 140
                )) {
                    c.Index++;

                    c.Emit(OpCodes.Ldloc, 138);
                    c.Emit(OpCodes.Ldloc, 139);

                    c.EmitDelegate<Action<int, int>>((num77, num78) => {
                        Item recipe = null;

                        if (Main.LocalPlayer.adjShimmer && Main.focusRecipe >= 0 && Main.focusRecipe < Main.availableRecipe.Length) {
                            int recipeIndex = Main.availableRecipe[Main.focusRecipe];
                            recipe = Main.recipe[recipeIndex].createItem;

                            bool canHavePrefixes = recipe.CanHavePrefixes();
                            int shimmerEquivalentType = ItemID.Sets.ShimmerCountsAsItem[recipe.type] != -1 ? ItemID.Sets.ShimmerCountsAsItem[recipe.type] : recipe.type;
                            bool canBeDecrafted = !ShimmerTransforms.IsItemTransformLocked(shimmerEquivalentType);

                            if (!(canHavePrefixes && canBeDecrafted))
                                recipe = null;
                        }

                        reforgePickUI.reforgeList.selectedRecipe = recipe;
                        if (recipe == null) return;

                        reforgePickUI.SetPositionValues(num77, num78);
                        ui.Draw(Main.spriteBatch, lastUpdateUiGameTime);
                    });
                } else {
                    throw new Exception();
                }
            } catch {
                MonoModHooks.DumpIL(ModContent.GetInstance<ShimmerReforgePick>(), il);
            }
        }

        public override void UpdateUI(GameTime gameTime) {
            lastUpdateUiGameTime = gameTime;
            ui?.Update(gameTime);
        }
    }

    class ReforgePickUI : UIState {
        private UIImage button;
        internal ReforgeList reforgeList;

        private int num77;
        private int num78;
        private bool showList;

        public override void OnInitialize() {
            button = new(Asset<Texture2D>.Empty);
            button.OnLeftClick += (evt, listeningElement) => {
                if (Main.recBigList) {
                    Main.recBigList = false;
                    showList = true;
                } else showList = !showList;
                SoundEngine.PlaySound(SoundID.MenuTick);
            };
            button.Color = new Color(255, 127, 244);
            Append(button);

            reforgeList = new();
            reforgeList.Activate();
            Append(reforgeList);
        }

        public override void Update(GameTime gameTime) {
            if (reforgeList.selectedRecipe == null || !showList || !Main.playerInventory || Main.recBigList) {
                reforgeList.Deactivate();
                RemoveChild(reforgeList);
            }

            base.Update(gameTime);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch) {
            if (button.IsMouseHovering) Main.instance.MouseText("Shimmer Reforge Pick");
            button.SetImage(button.IsMouseHovering ? TextureAssets.Reforge[1] : TextureAssets.Reforge[0]);
            //15 pixels fewer each since crafting toggle is drawn with its origin centered
            button.Left = new StyleDimension(num77 + 17, 0);
            button.Top = new StyleDimension(num78 - 15, 0);

            if (reforgeList.selectedRecipe != null) reforgeList.SetUIPrefixList();

            if (button.ContainsPoint(Main.MouseScreen))
                Main.LocalPlayer.mouseInterface = true;

            if (showList && !Main.recBigList) {
                reforgeList.Activate();
                Append(reforgeList);

                //No using items while on the active list
                if (reforgeList.ContainsPoint(Main.MouseScreen))
                    Main.LocalPlayer.mouseInterface = true;
            }
        }

        internal void SetPositionValues(int num77, int num78) {
            this.num77 = num77;
            this.num78 = num78;
        }
    }

    class ReforgeList : UIPanel {
        private UIList list;
        private UIScrollbar scrollbar;

        private Vector2 offset;
        private bool dragging;
        private int currentItemType = -1;

        internal Item selectedRecipe;
        internal int lastSelectedRecipeType;
        internal int desiredPrefix = -1;

        private static Color basePanelColor = new Color(63, 82, 151) * 0.8f;
        private static Color lighterPanelColor = new Color(76, 99, 181) * 0.8f;

        public override void OnInitialize() {
            Width.Set(300, 0);
            Height.Set(400, 0);
            Left.Set(250, 0);
            Top.Set(315, 0);

            list = [];
            list.Width.Set(-30f, 1f); //30 pixels for the scrollbar
            list.Height.Set(0, 1f);
            list.ListPadding = 5f;
            Append(list);

            scrollbar = new() {
                HAlign = 1f
            };
            scrollbar.Height.Set(0f, 1f);
            Append(scrollbar);
            list.SetScrollbar(scrollbar);
        }

        public override void LeftMouseDown(UIMouseEvent evt) {
            if (list.ContainsPoint(Main.MouseScreen) || scrollbar.ContainsPoint(Main.MouseScreen))
                return;

            offset = new Vector2(evt.MousePosition.X - Left.Pixels, evt.MousePosition.Y - Top.Pixels);
            dragging = true;
        }

        public override void LeftMouseUp(UIMouseEvent evt) {
            if (!dragging) return;
            Vector2 end = evt.MousePosition;
            dragging = false;

            Left.Set(end.X - offset.X, 0f);
            Top.Set(end.Y - offset.Y, 0f);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch) {
            if (dragging) {
                Left.Set(Main.mouseX - offset.X, 0f);
                Top.Set(Main.mouseY - offset.Y, 0f);
            }

            Rectangle parentSpace = Parent.GetDimensions().ToRectangle();
            if (!GetDimensions().ToRectangle().Intersects(parentSpace)) {
                dragging = true;
            }

            base.DrawSelf(spriteBatch);

            if (IsMouseHovering)
                PlayerInput.LockVanillaMouseScroll("ShimmerReforgePick/ReforgePicker");
        }

        internal void SetUIPrefixList() {
            if (currentItemType == selectedRecipe.type) return;

            currentItemType = selectedRecipe.type;
            list.Clear();

            List<int> prefixList = GetPrefixList();

            foreach (int i in prefixList) {
                string name;

                if (i < PrefixID.Count) name = Lang.prefix[i].Value;
                else name = PrefixLoader.GetPrefix(i).DisplayName.Value;

                UITextPanel<string> button = new(name);
                button.Width.Set(0f, 1f);
                button.Height.Set(20f, 0f);

                button.OnLeftClick += (evt, element) => {
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    desiredPrefix = i;
                };

                button.OnUpdate += (element) => {
                    if (desiredPrefix == i)
                        SetButtonColor(ref element, ColorContext.Selected);
                    else if (!element.ContainsPoint(Main.MouseScreen))
                        SetButtonColor(ref element, ColorContext.Default);
                };

                button.OnMouseOver += (evt, element) => {
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    SetButtonColor(ref element, ColorContext.Hover);
                };

                button.OnMouseOut += (evt, element) => SetButtonColor(ref element, ColorContext.Default);

                list.Add(button);
            }

            if (prefixList.Count != 0) desiredPrefix = prefixList[0];
        }

        private enum ColorContext {
            Default,
            Hover,
            Selected
        }

        private void SetButtonColor(ref UIElement button, ColorContext context) {
            var btn = (UITextPanel<string>)button;
            switch (context) {
                case ColorContext.Default:
                    btn.BackgroundColor = basePanelColor;
                    btn.BorderColor = Color.Black;
                    break;
                case ColorContext.Hover:
                    btn.BackgroundColor = lighterPanelColor;
                    btn.BorderColor = Color.Black;
                    break;
                case ColorContext.Selected:
                    btn.BackgroundColor = lighterPanelColor;
                    btn.BorderColor = Color.LightYellow;
                    break;
            }
        }

        private List<int> GetPrefixList() {
            List<int> prefixList = [];
            Dictionary<int, int> valueDict = [];

            for (int i = 1; i < PrefixID.Count; i++) {
                if (selectedRecipe.CanApplyPrefix(i)) {
                    prefixList.Add(i);

                    Item clone = selectedRecipe.Clone();
                    clone.Prefix(i);
                    int diff = clone.value - selectedRecipe.value;

                    valueDict.Add(i, diff);
                }
            }

            for (int i = PrefixID.Count + 1; i < PrefixLoader.PrefixCount; i++) {
                if (selectedRecipe.CanApplyPrefix(i)) {
                    prefixList.Add(i);

                    Item clone = selectedRecipe.Clone();
                    clone.Prefix(i);
                    int diff = clone.value - selectedRecipe.value;

                    valueDict.Add(i, diff);
                }
            }

            prefixList.Sort((prefix1, prefix2) => {
                return valueDict[prefix2].CompareTo(valueDict[prefix1]);
            });

            return prefixList;
        }
    }
}
