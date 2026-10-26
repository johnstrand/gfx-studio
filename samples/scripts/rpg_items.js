// Procedural RPG item metadata and color generator for rpg_items_sheet.gfx

const ITEM_TYPES = [
  { name: "Health Flask",    color: "#EF4444", glow: "#EF44443C", glyph: "♥", tier: "I"   },
  { name: "Mana Elixir",     color: "#3B82F6", glow: "#3B82F63C", glyph: "✦", tier: "II"  },
  { name: "Stamina Tonic",   color: "#10B981", glow: "#10B9813C", glyph: "⚡", tier: "I"   },
  { name: "Shadow Essence",  color: "#8B5CF6", glow: "#8B5CF63C", glyph: "▲", tier: "III" },
  { name: "Ruby Shard",      color: "#F43F5E", glow: "#F43F5E3C", glyph: "♦", tier: "II"  },
  { name: "Frost Sapphire",  color: "#06B6D4", glow: "#06B6D43C", glyph: "❄", tier: "IV"  },
  { name: "Emerald Rune",    color: "#22C55E", glow: "#22C55E3C", glyph: "★", tier: "III" },
  { name: "Amethyst Shard",  color: "#A855F7", glow: "#A855F73C", glyph: "◆", tier: "I"   },
  { name: "Iron Dagger",     color: "#94A3B8", glow: "#94A3B830", glyph: "†", tier: "I"   },
  { name: "Silver Blade",    color: "#E2E8F0", glow: "#E2E8F03C", glyph: "⚔", tier: "II"  },
  { name: "Golden Scythe",   color: "#F59E0B", glow: "#F59E0B3C", glyph: "☽", tier: "IV"  },
  { name: "Paladin Shield",  color: "#FBBF24", glow: "#FBBF243C", glyph: "🛡", tier: "III" },
  { name: "Fire Scroll",     color: "#F97316", glow: "#F973163C", glyph: "♨", tier: "II"  },
  { name: "Lightning Tome",  color: "#FDE047", glow: "#FDE0473C", glyph: "⚡", tier: "IV"  },
  { name: "Celestial Ring",  color: "#38BDF8", glow: "#38BDF83C", glyph: "☼", tier: "V"   },
  { name: "Crown of Ages",   color: "#FCD34D", glow: "#FCD34D3C", glyph: "♔", tier: "V"   }
];

export function getItemColor(index) {
  const i = Math.abs(Math.floor(index)) % ITEM_TYPES.length;
  return ITEM_TYPES[i].color;
}

export function getItemGlow(index) {
  const i = Math.abs(Math.floor(index)) % ITEM_TYPES.length;
  return ITEM_TYPES[i].glow;
}

export function getItemGlyph(index) {
  const i = Math.abs(Math.floor(index)) % ITEM_TYPES.length;
  return ITEM_TYPES[i].glyph;
}

export function getItemTier(index) {
  const i = Math.abs(Math.floor(index)) % ITEM_TYPES.length;
  return ITEM_TYPES[i].tier;
}
