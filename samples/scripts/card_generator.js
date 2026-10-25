// Procedural playing card deck generator for GfxStudio
const SUITS = ['♠', '♥', '♦', '♣'];
const SUIT_NAMES = ['Spades', 'Hearts', 'Diamonds', 'Clubs'];
const RANKS = ['A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K'];

export function getCardRank(cardIndex) {
  const idx = Math.floor(Math.abs(cardIndex)) % RANKS.length;
  return RANKS[idx];
}

export function getCardSuit(cardIndex) {
  const suitIdx = Math.floor(Math.floor(Math.abs(cardIndex)) / RANKS.length) % SUITS.length;
  return SUITS[suitIdx];
}

export function getCardSuitName(cardIndex) {
  const suitIdx = Math.floor(Math.floor(Math.abs(cardIndex)) / RANKS.length) % SUITS.length;
  return SUIT_NAMES[suitIdx];
}

export function getCardColor(cardIndex) {
  const suitIdx = Math.floor(Math.floor(Math.abs(cardIndex)) / RANKS.length) % SUITS.length;
  // Hearts (1) and Diamonds (2) are red, Spades (0) and Clubs (3) are dark
  return (suitIdx === 1 || suitIdx === 2) ? '#DC2626' : '#0F172A';
}

export function getCardLabel(cardIndex) {
  return `${getCardRank(cardIndex)}${getCardSuit(cardIndex)}`;
}
