import type { Item } from "../../types";
import { useKeydown } from "../../hooks/useKeydown";
import { ModalOverlay } from "./ModalOverlay";
import { CloseButton } from "../ui/CloseButton";
import { ItemHeader } from "./ItemHeader";
import { StatGrid } from "./StatGrid";
import { Description } from "./Description";

export function ItemModal({
  item,
  types,
  onClose,
}: {
  item: Item;
  types: Record<string, string>;
  onClose: () => void;
}) {
  useKeydown("Escape", onClose);
  return (
    <ModalOverlay onClose={onClose}>
      <CloseButton onClick={onClose} />
      <ItemHeader item={item} types={types} />
      {item.det && <StatGrid det={item.det} />}
      {item.det?.desc && <Description text={item.det.desc} />}
    </ModalOverlay>
  );
}
