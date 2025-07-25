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
        internal UserInterface ui;
        internal ReforgePickUI reforgePickUI;
        private GameTime lastUpdateUiGameTime;

        public override void Load() {
            if (!Main.dedServ) {
                IL_Main.DrawInventory += DrawReforgeUI;
                IL_Main.CraftItem += PickReforge;

                ui = new UserInterface();
                reforgePickUI = new ReforgePickUI();
                reforgePickUI.Activate();
                ui.SetState(reforgePickUI);
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

                            if (!(recipe.CanHavePrefixes() || (recipe.ModItem != null && ItemLoader.CanReforge(recipe))))
                                recipe = null;
                        }

                        if (recipe == null) return;

                        reforgePickUI.SetPositionValues(num77, num78);
                        reforgePickUI.SetSelectedRecipe(recipe);
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
                showList = !showList;
                SoundEngine.PlaySound(SoundID.MenuTick);
            };
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
            button.SetImage(button.IsMouseHovering ? TextureAssets.Reforge[1] : TextureAssets.Reforge[0]);
            //15 pixels fewer each since crafting toggle is drawn with its origin centered
            button.Left = new StyleDimension(num77 + 17, 0);
            button.Top = new StyleDimension(num78 - 15, 0);

            if (showList && !Main.recBigList) {
                reforgeList.Activate();
                Append(reforgeList);
            }

            if (reforgeList.ContainsPoint(Main.MouseScreen) || button.ContainsPoint(Main.MouseScreen))
                Main.LocalPlayer.mouseInterface = true;
        }

        internal void SetPositionValues(int num77, int num78) {
            this.num77 = num77;
            this.num78 = num78;
        }

        internal void SetSelectedRecipe(Item item) {
            if (reforgeList.selectedRecipe != null) reforgeList.lastSelectedRecipeType = reforgeList.selectedRecipe.type;
            reforgeList.selectedRecipe = item;
        }
    }

    class ReforgeList : UIPanel {
        private UIList list;
        private UIScrollbar scrollbar;

        private Vector2 offset;
        private bool dragging;

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
            list.Width.Set(-30f, 1f);
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

        protected override void DrawChildren(SpriteBatch spriteBatch) {
            SetPrefixList();

            base.DrawChildren(spriteBatch);
        }

        private void SetPrefixList() {
            if (lastSelectedRecipeType == selectedRecipe.type && list.Count > 0) return;

            desiredPrefix = -1;
            list.Clear();

            List<int> prefixList = [];
            Dictionary<int, int> valueDict = [];


            for (int i = 1; i < PrefixID.Search.Count; i++) {
                if (selectedRecipe.CanApplyPrefix(i)) {
                    prefixList.Add(i);

                    Item clone = selectedRecipe.Clone();
                    clone.Prefix(i);
                    int diff = clone.value - selectedRecipe.value;

                    valueDict.Add(i, diff);
                }
            }

            prefixList.Sort((prefix1, prefix2) => {
                if (valueDict[prefix1] > valueDict[prefix2]) return -1;
                else return 1;
            });

            foreach (int i in prefixList) {
                UITextPanel<string> button = new(Lang.prefix[i].Value);
                button.Width.Set(0f, 1f);
                button.Height.Set(20f, 0f);

                button.OnLeftClick += (evt, element) => {
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    desiredPrefix = i;
                };

                button.OnUpdate += (element) => {
                    var el = (UITextPanel<string>)element;
                    if (desiredPrefix == i)
                        el.BackgroundColor = lighterPanelColor;
                    else if (!el.ContainsPoint(Main.MouseScreen)) el.BackgroundColor = basePanelColor;
                };

                button.OnMouseOver += (evt, element) => {
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    var el = (UITextPanel<string>)element;
                    el.BackgroundColor = lighterPanelColor;
                };

                button.OnMouseOut += (evt, element) => {
                    var el = (UITextPanel<string>)element;
                    el.BackgroundColor = basePanelColor;
                };

                list.Add(button);
            }

            desiredPrefix = prefixList[0];
        }
    }
}
