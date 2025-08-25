using UnityEngine;

public class SelectionManagerUI : MonoBehaviour
{
    [SerializeField] private RectTransform selectionAreaTransform;
    [SerializeField] private RectTransform canvas;

    private void Start()
    {
        SelectionManager.Instance.OnSelectionAreaStart += SelectionManager_OnSelectionAreaStart;
        SelectionManager.Instance.OnSelectionAreaEnd += SelectionManager_OnSelectionAreaEnd;

        selectionAreaTransform.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (selectionAreaTransform.gameObject.activeSelf)
        {
            UpdateVisual();
        }
    }

    private void SelectionManager_OnSelectionAreaStart(object sender, System.EventArgs e)
    {
        selectionAreaTransform.gameObject.SetActive(true);
        UpdateVisual();
    }

    private void SelectionManager_OnSelectionAreaEnd(object sender, System.EventArgs e)
    {
        selectionAreaTransform.gameObject.SetActive(false);
    }

    private void UpdateVisual()
    {
        Rect selectionAreaRect = SelectionManager.Instance.GetSelectionAreaRect();

        float canvasScale = canvas.transform.localScale.x; //For changing aspect ratios
        selectionAreaTransform.anchoredPosition = new Vector2(selectionAreaRect.x, selectionAreaRect.y) / canvasScale;
        selectionAreaTransform.sizeDelta = new Vector2(selectionAreaRect.width, selectionAreaRect.height) / canvasScale;
    }
}
