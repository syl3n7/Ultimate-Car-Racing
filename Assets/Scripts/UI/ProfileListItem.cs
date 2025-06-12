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
    
    public event Action<ProfileData> OnProfileSelected;
    public event Action<ProfileData> OnProfileDeleted;
    
    private ProfileData profileData;
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
    
    public void Initialize(ProfileData profile)
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
    
    public ProfileData GetProfileData()
    {
        return profileData;
    }
}

/// <summary>
/// Enhanced ProfileData class with additional functionality
/// </summary>
[System.Serializable]
public class ProfileData
{
    public string name;
    public string id;
    public string lastPlayed;
    public string password; // Store encrypted password for local profile management
    public bool hasPassword; // Flag to indicate if password is set
    
    public ProfileData(string name, string id)
    {
        this.name = name;
        this.id = id;
        this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.password = "";
        this.hasPassword = false;
    }
    
    public ProfileData(string name, string id, string password)
    {
        this.name = name;
        this.id = id;
        this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.password = password;
        this.hasPassword = !string.IsNullOrEmpty(password);
    }
    
    public void SetPassword(string encryptedPassword)
    {
        this.password = encryptedPassword;
        this.hasPassword = !string.IsNullOrEmpty(encryptedPassword);
    }
    
    public void UpdateLastPlayed()
    {
        this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
