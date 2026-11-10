export interface BoardGame {
  id: number;
  name: string;
  category: string;
  minPlayers: number;
  maxPlayers: number;
  rentalPrice: number;
  isAvailable: boolean;
}
