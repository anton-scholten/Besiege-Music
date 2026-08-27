namespace OrchestraMod
{
    /// <summary>
    /// Takes the skin picker out of a block's mapper. Every block here is its own
    /// mesh and its own texture with nothing to swap to, so the row is only ever an
    /// empty choice -- a piano in a wooden skin is a piano with its lid replaced by
    /// a plank.
    ///
    /// **Not via `BlockPrefab.SkinCanBeChanged`.** That looks like the flag for it,
    /// and `BlockMapper.RefreshLists` does read it -- but so does `BlockPrefab.SetIcons`,
    /// which skips `VisualController.SetPrefabIcons()` when it is false. That call
    /// is what puts the block's own mesh and material on its button in the block
    /// menu: without it the button keeps `BlockLoader.LoadingMaterial`, which
    /// `BlockButtonCreator` painted on while the mod's resources were still loading.
    /// The block in the menu goes back to the loading texture the moment anything
    /// repaints it -- `BlockButtonControl.Set` on a click, for one.
    ///
    /// This is Special Effects' `Skins.Hide`, kept in step with it.
    ///
    /// The control is hidden instead: `GenericController.CreateContainers` skips any
    /// MapperType with `DisplayInMapper` false. It has to exist before the mapper
    /// first opens or the game builds it there and shows it once, so this makes the
    /// same call `RefreshLists` would; `RefreshLists` then takes its reuse path,
    /// which leaves `DisplayInMapper` alone.
    /// </summary>
    public static class Skins
    {
        /// <summary>The key the game itself gives this control. Kept the same so a
        /// machine saved before this change still finds its stored value.</summary>
        private const string SkinKey = "_CurrentSkin";

        public static void Hide(BlockBehaviour block)
        {
            if (block == null)
            {
                return;
            }

            if (block.Visual == null)
            {
                BlockVisualController visuals = block.VisualController;
                if (visuals == null || visuals.Options == null
                    || visuals.Options.Count == 0)
                {
                    return;
                }
                block.Visual = new MVisual(visuals,
                    visuals.Options.IndexOf(visuals.selectedSkin),
                    visuals.Options, SkinKey, null);
            }

            block.Visual.DisplayInMapper = false;
        }
    }
}
