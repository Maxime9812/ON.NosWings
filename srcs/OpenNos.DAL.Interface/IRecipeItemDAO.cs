﻿/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using System.Collections.Generic;
using OpenNos.Data;

namespace OpenNos.DAL.Interface
{
    public interface IRecipeItemDAO : IMappingBaseDAO
    {
        #region Methods

        RecipeItemDTO Insert(RecipeItemDTO recipeitem);

        IEnumerable<RecipeItemDTO> LoadAll();

        RecipeItemDTO LoadById(short RecipeItemId);

        IEnumerable<RecipeItemDTO> LoadByRecipe(short recipeId);

        IEnumerable<RecipeItemDTO> LoadByRecipeAndItem(short recipeId, short itemVNum);

        #endregion
    }
}