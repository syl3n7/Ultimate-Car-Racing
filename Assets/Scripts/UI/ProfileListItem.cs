using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Manages individual profile list items with select and delete functionality
/// </summary>
public class ProfileListItem : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI infoText;
    public Button selectButton;
    public Button deleteButton;
    
    [Header("Visual Styling")]
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(0.8f, 0.9f, 1f);
    public Color deleteColor = new Color(1f, 0.8f, 0.8f);
    
    public event Action<UIManager.ProfileData> OnProfileSelected;
    public event Action<UIManager.ProfileData> OnProfileDeleted;
    
    private UIManager.ProfileData profileData;
    private Image backgroundImage;
    private bool isSelected = false;
    
    void Awake()
    {
        backgroundImage = GetComponent<Image>();
        if (backgroundImage == null)
        {
            backgroundImage = gameObject.AddComponent<Image>();
        }
        
        // Set up button listeners
        if (selectButton != null)
        {
            selectButton.onClick.AddListener(OnSelectClicked);
        }
        
        if (deleteButton != null)
        {
            deleteButton.onClick.AddListener(OnDeleteClicked);
        }
    }
    
    public void Initialize(UIManager.ProfileData profile)
    {
        profileData = profile;
        
        if (nameText != null)
        {
            nameText.text = profile.name;
        }
        
        if (infoText != null)
        {
            infoText.text = $"Last played: {profile.lastPlayed}";
        }
        
        // Update visual state
        UpdateVisualState();
    }
    
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisualState();
    }
    
    private void UpdateVisualState()
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = isSelected ? selectedColor : normalColor;
        }
    }
    
    private void OnSelectClicked()
    {
        OnProfileSelected?.Invoke(profileData);
    }
    
    private void OnDeleteClicked()
    {
        // Show visual feedback
        if (backgroundImage != null)
        {
            backgroundImage.color = deleteColor;
        }
        
        // Invoke delete event
        OnProfileDeleted?.Invoke(profileData);
    }
    
    public UIManager.ProfileData GetProfileData()
    {
        return profileData;
    }
}

/// <summary>

