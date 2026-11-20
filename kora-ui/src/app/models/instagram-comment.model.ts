export interface InstagramCommentLog {
  id: number;
  commentId: string;
  mediaId?: string | null;
  username: string;
  commentText: string;
  publicReplyText?: string | null;
  privateDmSent: boolean;
  createdAt: string;
}

export interface SetupIceBreakersResult {
  success: boolean;
  message: string;
}
