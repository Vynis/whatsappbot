export interface BoardGame {
  id: number;
  name: string;
  category: string;
  minPlayers: number;
  maxPlayers: number;
  rentalPrice: number;
  salePrice?: number | null;
  aquisitionPrice?: number | null;
  linkLudopedia?: string | null;
  copyName?: string | null;
  slug?: string | null;
  status?: string | null;
  comments?: string | null;
  isAvailable: boolean;
}
