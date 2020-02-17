﻿using System;

namespace ManagedDoom
{
    public sealed class ThinkerPool
    {
        private World world;

        public ThinkerPool(World world)
        {
            this.world = world;
        }

        public Mobj RentMobj()
        {
            return new Mobj(world);
        }

        public void Return(Thinker thinker)
        {
        }
    }
}