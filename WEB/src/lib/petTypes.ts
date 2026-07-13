// Item type ids for the mount/pet domain (see script/lib/itemtypes.ts).
export const MOUNT_ITEM_TYPES = [12, 21]; // IT_PET, IT_SADDLE
export const PET_ITEM_TYPES = [22, 23, 24]; // Companion, Companion Costume, Companion Item

// Every item type in the mount/pet domain — used to scope the DB-vs-TCD diff.
export const MOUNT_PET_ITEM_TYPES = [...MOUNT_ITEM_TYPES, ...PET_ITEM_TYPES];

export const isMountItem = (t: number): boolean => MOUNT_ITEM_TYPES.includes(t);
export const isPetItem = (t: number): boolean => PET_ITEM_TYPES.includes(t);
